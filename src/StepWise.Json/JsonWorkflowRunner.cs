using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using StepWise.Http;

namespace StepWise.Json;

/// <summary>
/// Pure workflow execution engine. Callers supply request definitions and target base URLs;
/// executes all steps, evaluates assertions, and returns a WorkflowResult.
/// No xUnit dependency. Runnable from tests, CLI, or API.
/// </summary>
public class JsonWorkflowRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // ── Loading ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a workflow file and merges step definitions from <paramref name="requestPaths"/>
    /// (resolved relative to the workflow file's directory when not absolute).
    /// </summary>
    public static (WorkflowDefinition Workflow, Dictionary<string, StepDefinition> StepDefs) Load(
        string workflowPath,
        IReadOnlyList<string> requestPaths)
    {
        var workflowJson = File.ReadAllText(workflowPath);
        var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(workflowJson, JsonOptions)
            ?? throw new JsonWorkflowException($"Failed to deserialize workflow from '{workflowPath}'.");

        var workflowDir = Path.GetDirectoryName(Path.GetFullPath(workflowPath))!;
        var stepDefs = LoadRequestFiles(requestPaths, workflowDir);

        return (workflow, stepDefs);
    }

    /// <summary>
    /// Loads a list of .requests.json files and merges their step definitions.
    /// Paths are resolved relative to baseDir.
    /// </summary>
    public static Dictionary<string, StepDefinition> LoadRequestFiles(
        IReadOnlyList<string> requestPaths,
        string baseDir)
    {
        var merged = new Dictionary<string, StepDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in requestPaths)
        {
            var fullPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(baseDir, relativePath);

            var json = File.ReadAllText(fullPath);
            var requests = JsonSerializer.Deserialize<RequestsDefinition>(json, JsonOptions)
                ?? throw new JsonWorkflowException($"Failed to deserialize requests from '{fullPath}'.");

            foreach (var (name, def) in requests.Steps)
                merged[name] = def;
        }

        return merged;
    }

    public static Dictionary<string, string> LoadTargets(string targetsPath)
    {
        var json = File.ReadAllText(targetsPath);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
            ?? throw new JsonWorkflowException($"Failed to deserialize targets from '{targetsPath}'.");
    }

    // ── Running ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a workflow given pre-loaded step definitions and target base URLs by name.
    /// </summary>
    public static async Task<WorkflowResult> RunAsync(
        WorkflowDefinition workflow,
        Dictionary<string, StepDefinition> stepDefs,
        Dictionary<string, string> targets)
    {
        var baseUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, url) in targets)
            baseUrls[key] = url;

        var captures = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var stepResults = new List<StepResult>();

        foreach (var invocation in workflow.Steps)
        {
            if (invocation.Step is not null)
            {
                var result = await ExecuteStepAsync(invocation, stepDefs, baseUrls, captures);
                stepResults.Add(result);
            }
            else if (invocation.Build is not null)
            {
                var result = BuildItem(invocation, stepDefs, captures);
                stepResults.Add(result);
            }
            else if (invocation.Poll is not null)
            {
                var result = await PollStepAsync(invocation, stepDefs, baseUrls, captures);
                stepResults.Add(result);
            }
            else
            {
                throw new JsonWorkflowException("Each step must have 'step', 'build', or 'poll'.");
            }
        }

        var assertionErrors = workflow.Assertions is not null
            ? EvaluateAssertions(workflow.Assertions, captures)
            : [];

        return new WorkflowResult(
            workflow.Name,
            assertionErrors.Count == 0,
            stepResults,
            assertionErrors,
            captures);
    }

    /// <summary>
    /// Loads the workflow and request and targets files from disk.
    /// </summary>
    public static Task<WorkflowResult> RunAsync(
        string workflowPath,
        IReadOnlyList<string> requestPaths,
        string? targetsPath = null)
    {
        var (workflow, stepDefs) = Load(workflowPath, requestPaths);
        var targets = LoadTargets(targetsPath);
        return RunAsync(workflow, stepDefs, targets);
    }

    // ── Step execution ───────────────────────────────────────────────────────

    private static async Task<StepResult> ExecuteStepAsync(
        StepInvocation invocation,
        Dictionary<string, StepDefinition> stepDefs,
        Dictionary<string, string> baseUrls,
        Dictionary<string, object?> captures)
    {
        var stepName = invocation.Step!;

        if (!stepDefs.TryGetValue(stepName, out var stepDef))
            throw new JsonWorkflowException(
                $"Step '{stepName}' not found in loaded request files. " +
                $"Available: [{string.Join(", ", stepDefs.Keys)}]");

        if (!baseUrls.TryGetValue(stepDef.Target, out var baseUrl))
            throw new JsonWorkflowException(
                $"Target '{stepDef.Target}' not found. " +
                $"Available: [{string.Join(", ", baseUrls.Keys)}]");

        var resolvedFields = MergeAndResolve(stepDef.Defaults, invocation.With, captures);
        var method = new HttpMethod(stepDef.Method.ToUpper());
        var applyAuth = BuildAuthApplier(stepDef.Auth, captures);

        var responseJson = await HttpExecutor.SendAsync(
            baseUrl, method, stepDef.Path, resolvedFields, applyAuth);

        var responseDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            responseJson, HttpExecutor.JsonOptions);

        var captureName = invocation.CaptureAs ?? stepName;
        captures[captureName] = responseDict;

        return new StepResult(captureName, responseDict);
    }

    private static StepResult BuildItem(
        StepInvocation invocation,
        Dictionary<string, StepDefinition> stepDefs,
        Dictionary<string, object?> captures)
    {
        var buildName = invocation.Build!;

        if (!stepDefs.TryGetValue(buildName, out var stepDef))
            throw new JsonWorkflowException(
                $"Build step '{buildName}' not found in loaded request files.");

        var accumulationKey = stepDef.AccumulateAs
            ?? throw new JsonWorkflowException(
                $"Build step '{buildName}' must specify 'accumulateAs' in its step definition.");

        var resolvedFields = MergeAndResolve(stepDef.Defaults, invocation.With, captures);
        if (!captures.TryGetValue(accumulationKey, out var existing) ||
            existing is not List<Dictionary<string, object?>> list)
        {
            list = [];
            captures[accumulationKey] = list;
        }
        list.Add(resolvedFields);

        return new StepResult(buildName, resolvedFields);
    }

    private static async Task<StepResult> PollStepAsync(
        StepInvocation invocation,
        Dictionary<string, StepDefinition> stepDefs,
        Dictionary<string, string> baseUrls,
        Dictionary<string, object?> captures)
    {
        var stepName = invocation.Poll!;
        var executeInvocation = new StepInvocation { Step = stepName, CaptureAs = invocation.CaptureAs, With = invocation.With };
        var deadline = DateTime.UtcNow.AddMilliseconds(invocation.TimeoutMs);

        while (true)
        {
            var result = await ExecuteStepAsync(executeInvocation, stepDefs, baseUrls, captures);

            if (invocation.Until is null || EvaluateAssertions([invocation.Until], captures).Count == 0)
                return result;

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new JsonWorkflowException(
                    $"Poll step '{stepName}' timed out after {invocation.TimeoutMs}ms.");

            await Task.Delay((int)Math.Min(invocation.IntervalMs, remaining.TotalMilliseconds));
        }
    }

    private static Dictionary<string, object?> MergeAndResolve(
        Dictionary<string, FieldValueDefinition>? defaults,
        Dictionary<string, FieldValueDefinition>? overrides,
        Dictionary<string, object?> captures)
    {
        var merged = new Dictionary<string, FieldValueDefinition>(StringComparer.OrdinalIgnoreCase);
        if (defaults is not null)
            foreach (var (k, v) in defaults) merged[k] = v;
        if (overrides is not null)
            foreach (var (k, v) in overrides) merged[k] = v;

        return merged.ToDictionary(
            kv => kv.Key,
            kv => JsonValueResolver.Resolve(kv.Value).Resolve(captures),
            StringComparer.OrdinalIgnoreCase);
    }

    // ── Auth ─────────────────────────────────────────────────────────────────

    private static Func<HttpRequestMessage, Task> BuildAuthApplier(
        AuthDefinition? auth,
        Dictionary<string, object?> captures)
    {
        if (auth is null || auth.Type == "none")
            return _ => Task.CompletedTask;

        if (auth.Type == "bearer")
        {
            return req =>
            {
                string? token = auth.From is not null
                    ? ResolveCapturePath(auth.From, captures)?.ToString()
                    : auth.Token;

                if (token is not null)
                    req.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                return Task.CompletedTask;
            };
        }

        if (auth.Type == "apikey")
        {
            return req =>
            {
                var keyValue = auth.Key is not null
                    ? JsonValueResolver.Resolve(auth.Key).Resolve(captures)?.ToString()
                    : null;

                if (keyValue is not null)
                {
                    if (auth.Header is not null)
                        req.Headers.TryAddWithoutValidation(auth.Header, keyValue);
                    else if (auth.QueryParam is not null)
                    {
                        var builder = new UriBuilder(req.RequestUri!);
                        builder.Query += (builder.Query.Length > 1 ? "&" : "") +
                            $"{auth.QueryParam}={Uri.EscapeDataString(keyValue)}";
                        req.RequestUri = builder.Uri;
                    }
                }
                return Task.CompletedTask;
            };
        }

        throw new JsonWorkflowException(
            $"Unknown auth type '{auth.Type}'. Supported: none, bearer, apikey.");
    }

    // ── Assertions ───────────────────────────────────────────────────────────

    private static List<string> EvaluateAssertions(
        List<AssertionDefinition> assertions,
        Dictionary<string, object?> captures)
    {
        var errors = new List<string>();

        foreach (var assertion in assertions)
        {
            if (assertion.Equal is { Count: 2 })
            {
                var left = ResolveAssertionExpr(assertion.Equal[0], captures);
                var right = ResolveAssertionExpr(assertion.Equal[1], captures);
                if (!string.Equals(left?.ToString(), right?.ToString(), StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Expected '{assertion.Equal[0]}' ({left}) to equal '{assertion.Equal[1]}' ({right}).");
            }
            else if (assertion.NotEqual is { Count: 2 })
            {
                var left = ResolveAssertionExpr(assertion.NotEqual[0], captures);
                var right = ResolveAssertionExpr(assertion.NotEqual[1], captures);
                if (string.Equals(left?.ToString(), right?.ToString(), StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Expected '{assertion.NotEqual[0]}' to not equal '{assertion.NotEqual[1]}' but both were '{left}'.");
            }
            else if (assertion.Single is not null)
            {
                var value = ResolveAssertionExpr(assertion.Single, captures);
                var count = CountItems(value);
                if (count != 1)
                    errors.Add($"Expected '{assertion.Single}' to have exactly 1 item but found {count}.");
            }
            else if (assertion.Empty is not null)
            {
                var value = ResolveAssertionExpr(assertion.Empty, captures);
                var count = CountItems(value);
                if (count != 0)
                    errors.Add($"Expected '{assertion.Empty}' to be empty but found {count} items.");
            }
            else if (assertion.NotEmpty is not null)
            {
                var value = ResolveAssertionExpr(assertion.NotEmpty, captures);
                var count = CountItems(value);
                if (count == 0)
                    errors.Add($"Expected '{assertion.NotEmpty}' to not be empty.");
            }
        }

        return errors;
    }

    private static object? ResolveAssertionExpr(string expr, Dictionary<string, object?> captures)
        => expr.Contains('.') ? ResolveCapturePath(expr, captures)
         : captures.TryGetValue(expr, out var v) ? v
         : (object?)expr;

    internal static object? ResolveCapturePath(string path, Dictionary<string, object?> captures)
    {
        var parts = path.Split('.', 2);
        if (!captures.TryGetValue(parts[0], out var captured)) return null;
        if (parts.Length == 1) return captured;

        if (captured is Dictionary<string, JsonElement> dict)
        {
            var key = dict.Keys.FirstOrDefault(k =>
                string.Equals(k, parts[1], StringComparison.OrdinalIgnoreCase));
            return key is null ? null : JsonValueResolver.JsonElementToObject(dict[key]);
        }

        var prop = captured?.GetType().GetProperty(parts[1],
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return prop?.GetValue(captured);
    }

    private static int CountItems(object? value)
    {
        if (value is System.Collections.IEnumerable enumerable and not string)
            return enumerable.Cast<object>().Count();
        if (value is JsonElement el && el.ValueKind == JsonValueKind.Array)
            return el.GetArrayLength();
        return value is null ? 0 : 1;
    }
}

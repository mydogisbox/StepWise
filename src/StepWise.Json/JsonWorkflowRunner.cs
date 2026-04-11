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
    /// Loads workflow files and indexes them by their <c>name</c> field for use as named sub-workflows.
    /// </summary>
    public static Dictionary<string, WorkflowDefinition> LoadNamedWorkflows(IReadOnlyList<string> paths)
    {
        var result = new Dictionary<string, WorkflowDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var json = File.ReadAllText(path);
            var wf = JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonOptions)
                ?? throw new JsonWorkflowException($"Failed to deserialize workflow from '{path}'.");
            result[wf.Name] = wf;
        }
        return result;
    }

    /// <summary>
    /// Executes a workflow given pre-loaded step definitions and target base URLs by name.
    /// </summary>
    public static async Task<WorkflowResult> RunAsync(
        WorkflowDefinition workflow,
        Dictionary<string, StepDefinition> stepDefs,
        Dictionary<string, string> targets,
        IReadOnlyDictionary<string, WorkflowDefinition>? namedWorkflows = null)
    {
        var baseUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, url) in targets)
            baseUrls[key] = url;

        var captures = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var stepResults = await ExecuteStepsAsync(workflow.Steps, stepDefs, baseUrls, captures, namedWorkflows ?? new Dictionary<string, WorkflowDefinition>());

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
        string? targetsPath = null,
        IReadOnlyList<string>? sharedWorkflowPaths = null)
    {
        var (workflow, stepDefs) = Load(workflowPath, requestPaths);
        var targets = LoadTargets(targetsPath);
        var namedWorkflows = sharedWorkflowPaths is { Count: > 0 }
            ? LoadNamedWorkflows(sharedWorkflowPaths)
            : new Dictionary<string, WorkflowDefinition>();
        return RunAsync(workflow, stepDefs, targets, namedWorkflows);
    }

    // ── Step execution ───────────────────────────────────────────────────────

    private static async Task<List<StepResult>> ExecuteStepsAsync(
        IEnumerable<StepInvocation> steps,
        Dictionary<string, StepDefinition> stepDefs,
        Dictionary<string, string> baseUrls,
        Dictionary<string, object?> captures,
        IReadOnlyDictionary<string, WorkflowDefinition> namedWorkflows)
    {
        var results = new List<StepResult>();
        foreach (var invocation in steps)
        {
            if (invocation.Step is not null)
                results.Add(await ExecuteStepAsync(invocation, stepDefs, baseUrls, captures));
            else if (invocation.Build is not null)
                results.Add(BuildItem(invocation, stepDefs, captures));
            else if (invocation.Poll is not null)
                results.Add(await PollStepAsync(invocation, stepDefs, baseUrls, captures));
            else if (invocation.Workflow is not null)
                results.AddRange(await ExecuteNestedWorkflowAsync(invocation.Workflow, stepDefs, baseUrls, captures, namedWorkflows));
            else
                throw new JsonWorkflowException("Each step must have 'step', 'build', 'poll', or 'workflow'.");
        }
        return results;
    }

    private static async Task<List<StepResult>> ExecuteNestedWorkflowAsync(
        string workflowRef,
        Dictionary<string, StepDefinition> stepDefs,
        Dictionary<string, string> baseUrls,
        Dictionary<string, object?> captures,
        IReadOnlyDictionary<string, WorkflowDefinition> namedWorkflows)
    {
        WorkflowDefinition nested;
        if (namedWorkflows.TryGetValue(workflowRef, out var named))
        {
            nested = named;
        }
        else
        {
            var json = File.ReadAllText(workflowRef);
            nested = JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonOptions)
                ?? throw new JsonWorkflowException($"Failed to deserialize nested workflow from '{workflowRef}'.");
        }
        return await ExecuteStepsAsync(nested.Steps, stepDefs, baseUrls, captures, namedWorkflows);
    }

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

        if (invocation.CaptureRequestAs is { } requestKey)
            captures[requestKey] = resolvedFields;

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

        var captureName = invocation.CaptureAs ?? buildName;
        captures[captureName] = resolvedFields;

        return new StepResult(captureName, resolvedFields);
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
        => (expr.Contains('.') || expr.Contains('[')) ? ResolveCapturePath(expr, captures)
         : captures.TryGetValue(expr, out var v) ? v
         : (object?)expr;

    internal static object? ResolveCapturePath(string path, Dictionary<string, object?> captures)
    {
        var segments = TokenizePath(path);
        if (segments.Count == 0) return null;

        if (!captures.TryGetValue(segments[0], out var current)) return null;

        for (int i = 1; i < segments.Count; i++)
        {
            if (current is null) return null;
            current = ResolveSegment(current, segments[i]);
        }

        return current;
    }

    // Splits a path like "step.items[0].name" into ["step", "items", "[0]", "name"]
    private static List<string> TokenizePath(string path)
    {
        var segments = new List<string>();
        foreach (var dotPart in path.Split('.'))
        {
            if (string.IsNullOrEmpty(dotPart)) continue;
            var bracketIdx = dotPart.IndexOf('[');
            if (bracketIdx < 0)
            {
                segments.Add(dotPart);
                continue;
            }
            if (bracketIdx > 0)
                segments.Add(dotPart[..bracketIdx]);
            var remaining = dotPart[bracketIdx..];
            while (remaining.Length > 0 && remaining[0] == '[')
            {
                var close = remaining.IndexOf(']');
                if (close < 0) break;
                segments.Add(remaining[..(close + 1)]);
                remaining = remaining[(close + 1)..];
            }
        }
        return segments;
    }

    private static object? ResolveSegment(object current, string segment)
    {
        // Array index segment, e.g. "[0]"
        if (segment.StartsWith('[') && segment.EndsWith(']'))
        {
            if (!int.TryParse(segment[1..^1], out var idx)) return null;
            return current switch
            {
                List<object?> list       => idx >= 0 && idx < list.Count ? list[idx] : null,
                object?[] arr            => idx >= 0 && idx < arr.Length ? arr[idx] : null,
                System.Collections.IList ilist => idx >= 0 && idx < ilist.Count ? ilist[idx] : null,
                JsonElement el when el.ValueKind == JsonValueKind.Array
                                         => idx >= 0 && idx < el.GetArrayLength()
                                            ? JsonValueResolver.JsonElementToObject(el[idx]) : null,
                _                        => null,
            };
        }

        // Property/key segment
        return current switch
        {
            Dictionary<string, object?> objDict => objDict.TryGetValue(
                objDict.Keys.FirstOrDefault(k =>
                    string.Equals(k, segment, StringComparison.OrdinalIgnoreCase)) ?? "",
                out var oval) ? oval : null,
            Dictionary<string, JsonElement> dict => dict.Keys.FirstOrDefault(k =>
                string.Equals(k, segment, StringComparison.OrdinalIgnoreCase)) is { } key
                ? JsonValueResolver.JsonElementToObject(dict[key]) : null,
            JsonElement el when el.ValueKind == JsonValueKind.Object =>
                el.TryGetProperty(segment, out var jprop)
                ? JsonValueResolver.JsonElementToObject(jprop) : null,
            _ => current.GetType().GetProperty(segment,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?.GetValue(current),
        };
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

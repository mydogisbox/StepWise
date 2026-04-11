# Write tests for a new API

## Title
New API Test Generation Skill

## Description
Generate test infrastructure for a new API by identifying command sequences, defining request/response types, and creating workflow JSON files for test execution.

## When to Use
- Designing tests for a new API endpoint or service
- Defining command contracts before implementation
- Creating integration test workflows for unimplemented APIs
- Setting up test scaffolding for workflow automation

## Steps
1. **Identify Commands**: List all API commands/actions the new endpoint supports
2. **Define Request Types**: For each command, create a `WorkflowRequest<TResponse>` with typed fields
3. **Define Response Types**: Create DTOs for responses with `FieldValue` properties
4. **Create Requests JSON**: Define request builders in `*.requests.json` with field value specs
5. **Create Workflow JSON**: Write `.workflow.json` files that sequence the commands
6. **Configure Auth**: Define authentication handlers in the requests JSON
7. **Wire to Test Runner**: Place files in sample project for `JsonWorkflowTestBase` execution

## Examples

```csharp
// Step 1: Identify commands - for a User API:
//   - Create: POST /users with email, name
//   - Login: POST /login with email, password
//   - Logout: POST /logout with token
//   - Get: GET  /users/{id}
//   - Update: PUT  /users/{id}
//   - Delete: DELETE /users/{id}

// Step 2-3: Define request/response types
public record CreateRequest(
    string Email,
    string Name,
    string Password
) : WorkflowRequest<UserResponse>

public record UserResponse(
    string Id,
    string Email,
    string Name,
    [FromValue<string>] string Token
) : IFieldValue<string>

// Step 4: Requests JSON
// Requests/users.requests.json
// {
//   "create": { "target": "users" },
//   "login": { "target": "users" }
// }

// Step 5: Workflow JSON
// WorkflowTests/CreateAndLogin.workflow.json
// {
//   "name": "CreateAndLogin",
//   "steps": [
//     { "step": "create" },
//     { "step": "login", "with": { "email": { "from": "create.Email" } } }
//   ],
//   "assertions": [
//     { "equal": ["login.Token", "response.Token"] }
//   ]
// }

// Build Request Example - accumulate multiple items
// WorkflowTests/CreateMultipleUsers.workflow.json
// {
//   "name": "CreateMultipleUsers",
//   "steps": [
//     { "build": "addUser", "with": { "email": { "static": "alice@example.com" }, "name": { "static": "Alice" } } },
//     { "build": "addUser", "with": { "email": { "static": "bob@example.com" }, "name": { "static": "Bob" } } },
//     { "step": "create", "with": { "count": { "static": 2 } }, "from": "__build__addUser" }
//   ]
// }

// Build with Generated Requests
public record CreateUserRequest(
    GeneratedUserGenerator UserGenerator
) : WorkflowRequest<UserResponse>

public record GeneratedUserGenerator(string Email, string Name) : IFieldValue<GeneratedUser>
{
    public User Generate(WorkflowContext context)
        => new(context.Get<GeneratedUser>("__build__UserGenerator").Email, Name);
}
```

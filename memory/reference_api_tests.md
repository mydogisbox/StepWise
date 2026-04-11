---
name: reference_api_tests
description: How to create test infrastructure for new APIs using StepWise workflows
type: reference
---

New API Test Generation Skill

When designing tests for a new API endpoint:
1. Identify commands (Create, Login, Logout, Get, Update, Delete)
2. Define WorkflowRequest<TResponse> for each command
3. Define response DTOs with IFieldValue properties
4. Create requests.json files defining request builders
5. Create .workflow.json files sequencing commands
6. Configure auth handlers in requests.json
7. Place files in sample project for JsonWorkflowTestBase

Examples: CreateRequest, CreateUserRequest with GeneratedUserGenerator, requests.json patterns, workflow.json sequencing steps.
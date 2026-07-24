# LEB2 Scraper API - Project Guide

## 1. Project Overview

This project is an unofficial backend API for retrieving student, semester, class, and activity data from LEB2. It is built with:

- .NET 9
- ASP.NET Core Web API
- Controller-based HTTP endpoints
- Swashbuckle/OpenAPI for Swagger documentation
- Selenium WebDriver with Chrome/Chromium for browser-based scraping
- `HttpClient` for direct calls to LEB2 APIs

The project is not officially affiliated with KMUTT or the LEB2 system.

The solution uses a layered architecture with separate projects for HTTP presentation, application services, contracts, repositories, infrastructure, and shared entities. Keep policy and orchestration in services, HTTP concerns in controllers, and external-system details in repositories and infrastructure.

There is currently no local database, ORM, migration system, or persistence schema in this repository. Repository classes are adapters for LEB2 HTTP endpoints and browser scraping.

### Runtime and entrypoint

Application startup begins in `LEB2SCRAPPER/Program.cs`:

1. ASP.NET Core creates the application builder.
2. Controllers from `LEB2SCRAPPER.Presentation` are registered through an application part.
3. `ICoreAdapterManager`, `IServiceManager`, and `IRepositoryManager` are registered as scoped dependencies.
4. OpenAPI, Swagger, permissive CORS, and the global exception middleware are configured.
5. Controller routes are mapped and the application starts.

Swagger UI is exposed in the Development environment at:

- `/swagger`

The local launch profiles use:

- `http://localhost:5015`
- `https://localhost:7104`

The Docker image listens on port `8080` and runs in the Production environment by default. Swagger is therefore disabled in the default container configuration.

---

## 2. Project Structure

The main request flow is:

`Controller -> IServiceManager -> service -> ICoreAdapterManager -> IRepositoryManager -> repository -> Selenium/HttpService -> LEB2`

### High-level project map

- `LEB2SCRAPPER`
  - ASP.NET Core host, application startup, middleware, configuration, and launch profiles.
- `LEB2SCRAPPER.Presentation`
  - Controllers, action filters, HTTP validation, routes, and response metadata.
- `LEB2SCRAPPER.Service.Contracts`
  - Service interfaces and the `IServiceManager` contract.
- `LEB2SCRAPPER.Service`
  - Use-case orchestration, service implementations, `ServiceManager`, and `CoreAdapterManager`.
- `LEB2SCRAPPER.Contracts`
  - Repository interfaces and the `IRepositoryManager` contract.
- `LEB2SCRAPPER.Repository`
  - Concrete outbound adapters for LEB2 API calls and Selenium scraping.
- `LEB2SCRAPPER.Infrastructure.Contracts`
  - Technical contracts such as `IHttpService`.
- `LEB2SCRAPPER.Infrastructure`
  - Shared technical implementations, especially HTTP transport and JSON conversion.
- `LEB2SCRAPPER.Entity`
  - Request models, response models, DTOs, validation attributes, exceptions, and shared data structures.

### Host Layer (`LEB2SCRAPPER`)

Purpose:

- Composes the application.
- Registers dependencies and controllers.
- Configures the HTTP middleware pipeline.
- Hosts cross-cutting behavior such as global exception handling.

Important files:

- `Program.cs`
- `Extensions/MiddlewareExtensions.cs`
- `Middleware/GlobalExceptionMiddleware.cs`
- `Properties/launchSettings.json`
- `appsettings.json`

What it should NOT do:

- Contain feature-specific business logic.
- Call LEB2 endpoints directly.
- Duplicate controller, service, or repository behavior.

### Presentation Layer (`LEB2SCRAPPER.Presentation`)

Purpose:

- Exposes HTTP routes.
- Validates request bodies, route values, and required headers.
- Delegates use-case work to `IServiceManager`.
- Translates results into ASP.NET Core action responses.

Contains:

- `Controller/*`
- `Filters/ValidateModelAttribute.cs`
- `AssemblyReference.cs`

What it should do:

- Keep actions thin.
- Use DTOs and validation attributes for input validation.
- Declare expected response status codes for Swagger.
- Pass work to service contracts.

What it should NOT do:

- Instantiate repositories or transport clients.
- Use Selenium or `HttpClient` directly.
- Contain scraping selectors or external URL construction.
- Own application decision rules.

### Service Contract Layer (`LEB2SCRAPPER.Service.Contracts`)

Purpose:

- Defines feature-facing application service contracts.
- Exposes services through `IServiceManager`.

Contains:

- `Core/IServiceManager.cs`
- `Master/IActivityService.cs`
- `Master/IClassService.cs`
- `Master/ISemesterService.cs`
- `Master/IUserService.cs`

New service capabilities should be added to the relevant interface. A new service should also be exposed through `IServiceManager`.

### Service Layer (`LEB2SCRAPPER.Service`)

Purpose:

- Orchestrates application use cases.
- Coordinates repository operations.
- Keeps controllers independent of external-system implementation details.

Contains:

- `Master/*Service.cs`
- `Core/ServiceManager.cs`
- `CoreAdapterManager.cs`

`ServiceManager` lazily creates feature services. `CoreAdapterManager` supplies the repository manager to those services.

What it should NOT do:

- Depend on `ControllerBase`, `IActionResult`, or HTTP status codes.
- Contain Selenium selectors.
- Build raw outbound HTTP requests when an existing repository or `IHttpService` can perform that work.

### Repository Contract Layer (`LEB2SCRAPPER.Contracts`)

Purpose:

- Defines ports for retrieving data from external systems.
- Exposes repository contracts through `IRepositoryManager`.

Contains:

- `Repository/IActivityRepository.cs`
- `Repository/IScrapingRepository.cs`
- `Repository/IUserRepository.cs`
- `Repository/Core/IRepositoryManager.cs`

Contracts should describe the data operation needed by the application without exposing Selenium-specific or ASP.NET-specific types.

### Repository Layer (`LEB2SCRAPPER.Repository`)

Purpose:

- Implements repository contracts.
- Knows LEB2 URLs, request headers, browser navigation, and page selectors.
- Converts external responses into entity models.

Contains:

- `Master/ActivityRepository.cs`
- `Master/ScrapingRepository.cs`
- `Master/UserReposiroty.cs`
- `Core/RepositoryManager.cs`

`RepositoryManager` lazily creates repository implementations.

What it should NOT do:

- Return `IActionResult`.
- Decide HTTP response status codes.
- Contain controller-specific validation.
- Bypass existing transport helpers without a concrete need.

Note: `UserReposiroty.cs` is an existing filename typo; the class is correctly named `UserRepository`. Do not copy the typo into new types or filenames.

### Infrastructure Layers

`LEB2SCRAPPER.Infrastructure.Contracts` defines reusable technical contracts.

`LEB2SCRAPPER.Infrastructure` implements those contracts. The main implementation is `HttpService`, which:

- Sends outbound GET and POST requests.
- Serializes and deserializes snake_case JSON.
- Handles the date and boolean formats returned by LEB2.

Before changing outbound serialization or response mapping, read the complete `HttpService` implementation and the affected response models. Converter changes can affect every repository.

### Entity Layer (`LEB2SCRAPPER.Entity`)

Purpose:

- Holds models shared across layers.
- Defines DTOs, response shapes, validation attributes, and custom exceptions.

Contains:

- `Models/*`
- `DataTransferModels/*`
- `ModelsExtension/*`
- `ValidationAttributes/*`
- `Exceptions/*`

Entities must remain free of ASP.NET controller behavior, Selenium behavior, and direct outbound network calls.

---

## 3. Usage Guide

### Prerequisites

- .NET 9 SDK
- Chrome or Chromium compatible with the configured Selenium driver
- Network access to the LEB2 sign-in, application, and public API endpoints

No custom application environment variables are currently required.

Useful ASP.NET Core environment variables include:

- `ASPNETCORE_ENVIRONMENT`
  - Use `Development` to enable Swagger.
- `ASPNETCORE_URLS`
  - Overrides the listening URLs when needed.

Never commit credentials, LEB2 cookies, access tokens, or other secrets to source control or configuration files.

### Restore and build

```bash
dotnet restore LEB2SCRAPPER.sln
dotnet build LEB2SCRAPPER.sln
```

### Run locally

```bash
dotnet run --project LEB2SCRAPPER/LEB2SCRAPPER.csproj
```

Then open:

- `http://localhost:5015/swagger`
- `https://localhost:7104/swagger`

The exact active URL is printed by ASP.NET Core when the application starts.

### Run with Docker

```bash
docker build -t leb2scrapper-api .
docker run --rm -p 8080:8080 leb2scrapper-api
```

The default container runs as Production, so its Swagger UI is not enabled unless the environment configuration is explicitly changed.

### Current routes

- `POST /User/login`
  - Accepts LEB2 credentials and returns the mapped user profile.
- `POST /User/cookie`
  - Accepts LEB2 credentials and returns a scraped LEB2 session cookie.
- `GET /Semester`
  - Requires the LEB2 session value in the `Authorization` header.
- `GET /Class/{id}`
  - Requires the LEB2 session value in the `Authorization` header.
  - `id` is the semester ID.
- `POST /Activity`
  - Requires `userId` and `classId` in the request body.
  - Requires the LEB2 session value in the `Authorization` header.

The current `Authorization` header is passed through as an LEB2 session/cookie value. It is not a JWT authentication implementation and should not be documented or treated as one.

### Typical API flow

1. Call `POST /User/login` when user profile data is needed.
2. Call `POST /User/cookie` to obtain an LEB2 session cookie.
3. Treat the returned cookie as a secret.
4. Send that value in the `Authorization` header when requesting semesters.
5. Use a semester ID to request classes.
6. Use the user ID and class ID to request activities.

External LEB2 HTML, selectors, payloads, and response shapes can change independently of this repository. When an integration fails, inspect the current external contract before changing local models or scraping logic.

---

## 4. Layered Architecture in This Project

This repository separates HTTP delivery, use-case orchestration, and external integration details through contracts and manager facades.

### Dependency direction

The intended direction for feature work is:

- Controllers depend on service contracts.
- Services depend on repository contracts.
- Repositories implement repository contracts.
- Repositories use infrastructure contracts and implementations for technical operations.
- Entity models are shared data structures and should not depend on outer layers.

Some existing project references are broader than this ideal. Do not use those broad references as a reason to create new cross-layer coupling. Prefer the narrowest existing contract that supports the feature.

### Request lifecycle example

Activity request (`POST /Activity`):

1. `ActivityController` receives and validates `ActivityDto` and the `Authorization` header.
2. The controller calls `IServiceManager.ActivityService`.
3. `ActivityService` delegates through `IRepositoryManager.ActivityRepository`.
4. `ActivityRepository` calls the LEB2 activities endpoint through `IHttpService`.
5. `HttpService` applies the shared JSON converters and deserializes the response.
6. The result travels back through the service to the controller.
7. The controller returns the HTTP response.

### Why these boundaries matter

- Controllers remain small and focused on HTTP.
- Services can coordinate more than one external operation without leaking transport details.
- Repositories isolate changing LEB2 URLs, headers, payloads, and selectors.
- Shared transport behavior is changed in one place.
- Contracts make individual layers easier to replace and test.

### How to add a new feature

Use the narrowest set of steps that the feature actually needs:

1. Read the complete path of the closest existing feature.
2. Add or update entity models, DTOs, validation, and exceptions in `LEB2SCRAPPER.Entity`.
3. Define the required repository operation in `LEB2SCRAPPER.Contracts`.
4. Implement the operation in `LEB2SCRAPPER.Repository`, reusing `IHttpService` or the existing scraping adapter.
5. Expose a new repository through `IRepositoryManager` and `RepositoryManager` only when a new repository type is necessary.
6. Define the use case in `LEB2SCRAPPER.Service.Contracts`.
7. Implement the use case in `LEB2SCRAPPER.Service`.
8. Expose a new service through `IServiceManager` and `ServiceManager` only when a new service type is necessary.
9. Add a thin controller action with validation and response metadata.
10. Build the entire solution and verify the route in Swagger.

Do not create a new service or repository merely to wrap one method when an existing feature boundary already owns that behavior.

---

## 5. Code Style

Follow the style of the nearest related files, with the rules below taking priority for new or modified code.

### Naming

- Use PascalCase (UpperCamelCase) for classes, records, structs, enums, public properties, and methods.
- Prefix interfaces with `I`, for example `IActivityService`.
- Use camelCase for parameters and local variables.
- Use `_camelCase` for private instance fields.
- End asynchronous method names with `Async`.
- Use descriptive names based on the LEB2 domain. Do not introduce unexplained abbreviations.

### Braces

Use Allman-style braces. Opening braces belong on the line after the declaration or control statement. Always use braces for control-flow bodies, including one-line `if`, `else`, `for`, `foreach`, `while`, and `using` statements.

Bad:

```csharp
public interface exampleService {
    Task getExample(int id);
}
```

```csharp
public class exampleService : exampleService {
    public async Task getExample(int id) {}
}
```

```csharp
if (exampleId <= 0) {
    return null;
} else {
    await LoadExample();
}
```

Good:

```csharp
public interface IExampleService
{
    Task<Example?> GetExampleAsync(int exampleId);
}
```

```csharp
public class ExampleService : IExampleService
{
    public async Task<Example?> GetExampleAsync(int exampleId)
    {
        return await LoadExampleAsync(exampleId);
    }
}
```

```csharp
if (exampleId <= 0)
{
    return null;
}
else
{
    await LoadExampleAsync(exampleId);
}
```

### General style

- Preserve nullable reference type correctness; all projects enable nullable analysis.
- Prefer file-scoped namespaces in new files unless the nearest feature consistently uses block-scoped namespaces.
- Keep controllers thin and use dependency contracts rather than concrete repositories.
- Do not hide asynchronous I/O behind synchronous blocking calls.
- Keep external URLs, headers, selectors, and response mapping inside repository/infrastructure code.
- Add blank lines between distinct logical steps when it improves readability.
- Do not reformat an entire unrelated file while making a small feature change.
- Existing code is the primary reference for domain terminology and response mapping. Read it before naming new types or fields.

---

## 6. Rules

1. Do not change core composition files except for the smallest additive registration needed by the requested feature. Core composition files include `Program.cs`, `ServiceManager.cs`, `RepositoryManager.cs`, `CoreAdapterManager.cs`, middleware, and project files.

2. Do not invent missing domain or external-contract details. If a requested feature depends on information that cannot be discovered from the repository—such as an LEB2 endpoint, payload, response sample, authentication requirement, or expected output—stop and ask the user for that information before implementing the dependent work.

3. Change only files related to the requested feature. Work on an activity feature must not alter user, class, or semester behavior unless the feature explicitly requires that integration.

4. You may format files directly related to the change when they do not follow the style in this guide, but do not change unrelated logic while formatting.

5. Understand the complete related code path before changing it. For repository work, inspect its contract, service, controller, manager registration, entity models, and the relevant infrastructure helper. For outbound JSON work, read `HttpService` and its converters before editing models or mappings.

6. Reuse existing contracts, models, converters, filters, managers, and transport helpers before creating new ones. Add a new abstraction only when the existing resources do not support the requested behavior.

7. Follow the closest established implementation pattern. Use `User` for credential/profile flows, `Semester` and `Class` for Selenium flows, and `Activity` for direct authenticated LEB2 API flows.

8. For every new route, add appropriate routing, input validation, and `ProducesResponseType` metadata. Verify that the generated Swagger document reflects the new endpoint. There is no committed `openapi.yaml`; do not create one unless the user requests a committed API specification.

9. Do not introduce a database, ORM, schema, or migrations unless the user explicitly requests persistence and provides the required data model. This repository currently has no database layer.

10. Never log, commit, echo, or include real usernames, passwords, LEB2 cookies, authorization values, or session data in tests, fixtures, examples, error responses, or documentation. Use clearly fake placeholders.

11. Keep external integration details descriptive and maintainable. Use clear response-property and mapping names rather than short aliases. Centralize new URLs and repeated headers within the owning repository or infrastructure component.

12. Selenium changes must account for browser cleanup on both success and failure. Do not leave Chrome/Chromium processes running, and do not weaken headless/container compatibility without an explicit requirement.

13. Before completing a code change, run at least `dotnet build LEB2SCRAPPER.sln`. Run relevant tests when a test project exists or is added. If runtime behavior depends on live LEB2 access and cannot be verified safely, state that limitation clearly.

14. Do not upgrade packages, change target frameworks, rename existing public contracts, or alter deployment settings unless the requested feature requires it.

15. Put project documentation in `docs/`. Put auxiliary Markdown that is not project documentation in `etc/`. Root-level `AGENTS.md` and `README.md` are explicit exceptions.

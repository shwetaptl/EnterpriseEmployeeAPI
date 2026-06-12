# Architecture: Enterprise Employee Directory API

## Overview

The API is structured as a classic **n-tier architecture** with seven distinct project assemblies. Each tier has exactly one responsibility and communicates downward only through an interface contract. No layer holds a reference to any layer more than one step below it.

```
┌─────────────────────────────────────────────────────────────┐
│                     RestWebAPI                              │
│           (Controllers · Filters · App_Start)               │
└──────────────────────────┬──────────────────────────────────┘
                           │ depends on ↓
┌──────────────────────────▼──────────────────────────────────┐
│              BusinessServiceInterface                        │
│                   (ILoginService)                            │
└──────────────────────────┬──────────────────────────────────┘
                           │ implemented by ↓
┌──────────────────────────▼──────────────────────────────────┐
│                   BusinessService                            │
│                   (LoginService)                             │
└──────────────────────────┬──────────────────────────────────┘
                           │ depends on ↓
┌──────────────────────────▼──────────────────────────────────┐
│              DataServiceInterface                            │
│                (ILoginDataService)                           │
└──────────────────────────┬──────────────────────────────────┘
                           │ implemented by ↓
┌──────────────────────────▼──────────────────────────────────┐
│                    DataService                               │
│          (LoginDataService · DbService)                      │
└──────────────────────────┬──────────────────────────────────┘
                           │ depends on ↓
┌──────────────────────────▼──────────────────────────────────┐
│                     Database                                 │
│        (Context / EF · Ado / ADO.NET · DapperClass)         │
└─────────────────────────────────────────────────────────────┘
```

### Dependency Rule

The arrows point **downward only**. `RestWebAPI` knows `BusinessServiceInterface` — it does not know `BusinessService` exists. `BusinessService` knows `DataServiceInterface` — it does not know `LoginDataService` or `DbService` exist. This is enforced at compile time by project references, not just by convention.

---

## Layer Responsibilities

### RestWebAPI

The presentation layer. Handles HTTP concerns only:

- Routing (attribute routing via `[RoutePrefix]` / `[Route]`, convention routing in `WebApiConfig`)
- Deserializing request bodies and query parameters
- Returning `HttpResponseMessage` / `IHttpActionResult`
- No business logic — delegates immediately to `ILoginService`

**Key files:**
- `Controllers/LoginController.cs` — two endpoints: `POST UserLogin`, `GET GetEmployeeList`
- `Controllers/TokenController.cs` — JWT generation and validation (static utility; not called externally over HTTP)
- `App_Start/WebApiConfig.cs` — global filter registration, FluentValidation wiring
- `App_Start/NinjectWebCommon.cs` — DI container startup

### Filters (cross-cutting, applied globally)

Three filters are registered globally so every endpoint gets them automatically:

| Filter | Base Class | Responsibility |
|--------|-----------|----------------|
| `ValidateModelStateFilter` | `ActionFilterAttribute` | Returns HTTP 400 if `ModelState.IsValid == false` before the action runs |
| `CustomExceptionFilter` | `ExceptionFilterAttribute` | Catches any unhandled exception, returns HTTP 500 with a sanitized message |
| `JwtAuthenticationAttribute` | `AuthorizationFilterAttribute` | Applied per-endpoint (`[JwtAuthentication]`); validates Bearer token via `TokenController.ValidateToken()` |

`ValidateModelStateFilter` and `CustomExceptionFilter` are registered in `WebApiConfig.Register()`, meaning they apply to every API controller in the application regardless of future additions.

### BusinessServiceInterface / BusinessService

The business layer. Responsible for:

- Translating between business models (`BusinessModel`) and data models (`DataModel`) using static mapper methods
- Applying business rules (e.g., checking `userId != 0` before generating a token)
- Orchestrating calls to the data layer

`LoginService` constructor-injects `ILoginDataService` — it never instantiates `LoginDataService` directly.

### DataServiceInterface / DataService

The data access layer. Responsible for:

- Executing queries (stored procedures in the original design)
- Returning `DataModel` POCOs — no business objects, no HTTP types
- Choosing the ORM strategy (EF, Dapper, or ADO.NET) — the business layer is unaware of this choice

### DataModel

Plain C# objects (POCOs) that mirror the database schema. No business logic, no HTTP attributes. This is the data contract between the data layer and the business layer.

### BusinessModel

Request/response objects for the business layer. This is what the presentation layer passes down and receives back. Includes:

- `BOResponse<T>` — generic list envelope (`Code`, `Desc`, `Data: List<T>`)
- `BOResponseSingle<T>` — same for single items
- Static `Create()` mapper methods — each BO type knows how to construct itself from the corresponding `DataModel` type

### Database

Database infrastructure shared by all ORM strategies:

- `Context` — Entity Framework `DbContext`, named connection `"Entities"` in `Web.config`
- `Ado` — ADO.NET static class; `DbProviderFactory` pattern for ODBC database agnosticism
- `DapperClass` — returns an open `IDbConnection` used by Dapper queries

---

## Dependency Injection — Ninject

`NinjectWebCommon.cs` runs at application startup (via `WebActivatorEx.PreApplicationStartMethod`). It binds:

```csharp
kernel.Bind<ILoginService>().To<LoginService>();
kernel.Bind<ILoginDataService>().To<LoginDataService>();
```

This is the only place in the entire solution where a concrete class name appears across a layer boundary. To swap `LoginDataService` for a different ORM implementation, you change exactly this one line.

Ninject integrates with `System.Web.Http` via `NinjectDependencyResolver`, which means ASP.NET Web API's built-in constructor injection pipeline uses Ninject automatically — no `[Inject]` attributes needed on controllers.

---

## The Mapper Pattern

Each model boundary uses a static `Create()` factory method rather than a separate mapper class or AutoMapper:

```csharp
// In LoginBORequest (BusinessModel)
public static LoginRequest Create(LoginBORequest objBORequest)
{
    return new LoginRequest
    {
        UserName = objBORequest.username,
        Password = objBORequest.password,
        StrUDID  = objBORequest.udId
    };
}

// In EmployeeListBOResponse (BusinessModel)
public static BOResponse<EmployeeListBOResponse> Create(List<EmployeeMaster> lstResponse)
{
    // maps each EmployeeMaster → EmployeeListBOResponse
    // wraps in BOResponse<T>
}
```

**Why this over AutoMapper?**
- Zero configuration — the mapping is code, not convention
- Compile-time errors if a property is renamed
- No reflection overhead at startup
- The mapping logic lives next to the type it produces, making it easy to find

---

## Authentication Flow

```
POST /api/login/UserLogin
  └─ ValidateModelStateFilter: checks FluentValidation rules on LoginBORequest
       └─ LoginController.UserLogin()
            └─ LoginService.UserLoginInfo() → LoginDataService.UserLoginInfo()
                 └─ if userId != 0:
                      TokenController.GenerateToken(firstName, expireMinutes=2)
                        └─ creates JWT with ClaimTypes.Name claim
                           signed with HMAC-SHA256 using key from Web.config["JwtSecret"]
                      → returns token string as HTTP 200

GET /api/login/GetEmployeeList?userId=X
  └─ JwtAuthenticationAttribute: reads Authorization header
       └─ TokenController.ValidateToken(token)
            └─ GetPrincipal() validates signature + expiry (ClockSkew = TimeSpan.Zero)
                 └─ if valid: sets Thread.CurrentPrincipal to GenericPrincipal
                      → LoginController.GetEmployeeList() runs
                 └─ if invalid: HTTP 401 returned immediately
```

---

## Validation Strategy — Two Layers

The project demonstrates both validation approaches that existed in the ASP.NET Web API ecosystem:

**DataAnnotations** (commented out in `LoginBORequest`):
```csharp
// [Required]
// public string username { get; set; }
```
Simple, attribute-based, built into the framework. Suitable for straightforward required/length/format rules.

**FluentValidation** (active):
```csharp
public class LoginValidator : AbstractValidator<LoginBORequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.username).NotEmpty().WithMessage("UserName cannot be empty");
        RuleFor(x => x.password).NotEmpty().WithMessage("Password cannot be empty");
    }
}
```
Separates validation rules into a dedicated class. Supports complex conditional rules, cross-property validation, and custom validators. The `[Validator(typeof(LoginValidator))]` attribute on `LoginBORequest` wires it to the FluentValidation provider registered in `WebApiConfig`.

Both approaches feed into `ModelState`, which `ValidateModelStateFilter` checks before the action executes.

---

## Comparison with .NET 8

The same architectural concepts appear in modern .NET — the framework APIs differ, not the design:

| This Project (.NET Framework 4.6.1) | .NET 8 Equivalent |
|-------------------------------------|-------------------|
| Ninject `RegisterServices()` | `builder.Services.AddScoped<ILoginService, LoginService>()` |
| `System.Web.Http.ApiController` | `Microsoft.AspNetCore.Mvc.ControllerBase` |
| `WebApiConfig.Register()` + `Global.asax` | `WebApplication.CreateBuilder()` + `program.cs` |
| Custom `AuthorizationFilterAttribute` for JWT | `AddAuthentication().AddJwtBearer()` middleware |
| `ConfigurationManager.AppSettings` | `builder.Configuration["JwtSecret"]` |
| `ExceptionFilterAttribute` | `IExceptionHandler` or exception middleware |
| `FluentValidation.WebApi` registration | `AddFluentValidationAutoValidation()` |

The n-tier structure, interface-based DI, `BOResponse<T>` envelope, and static mapper pattern are framework-agnostic and carry over unchanged.

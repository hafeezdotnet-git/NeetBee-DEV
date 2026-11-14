# NEET BEE: configuration reference

Solution: `D:\Website\NEET_BEE_App_v1.0\backend\NeetBee.sln`

| Project | URL | Depends on |
|---|---|---|
| WebApplication (web app) | http://localhost:5100 | Gateway (called from the browser) |
| ApiGateway | http://localhost:5000 | AuthenticationService |
| AuthenticationService | http://localhost:5001 (localhost only) | SQL Server (`NEET_BEE`) |
| SQL Server | localhost, Windows Authentication | none |

Start order: SQL Server, then AuthenticationService, then ApiGateway, then WebApplication.

## 1. Prerequisites

- .NET 8 SDK (`dotnet --version` should report 8.x).
- SQL Server running on `localhost`, with your Windows account allowed to create databases.
- Windows PowerShell 5.1 or PowerShell 7 for the helper script.

## 2. SQL Server

Run once:

```powershell
sqlcmd -S localhost -E -i "D:\Website\NEET_BEE_App_v1.0\backend\Database\001_create_neet_bee.sql"
```

| Item | Value |
|---|---|
| Server | `localhost` |
| Authentication | Windows (`-E` / `Trusted_Connection=True`) |
| Database | `NEET_BEE` |
| Tables | `dbo.Users`, `dbo.RefreshTokens` |

## 3. Environment variables (secrets)

| Variable | Used by | Required | Value |
|---|---|---|---|
| `Authentication__Jwt__SigningKey` | AuthenticationService and ApiGateway | Yes (TokenBased mode) | Random string, 32+ characters. Both services must have the same value. |

Set it for the session:

```powershell
$env:Authentication__Jwt__SigningKey = "<your 32+ character random key>"
```

Or persist it for your user account (open new terminals afterwards):

```powershell
setx Authentication__Jwt__SigningKey "<your 32+ character random key>"
```

Generate a key:

```powershell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
```

## 4. AuthenticationService

File: `AuthenticationService\appsettings.json`

| Key | Value | Notes |
|---|---|---|
| `Kestrel:Endpoints:Http:Url` | `http://localhost:5001` | Keep on localhost; the gateway is the public entry point. |
| `ConnectionStrings:NeetBee` | `Server=localhost;Database=NEET_BEE;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;` | Windows Authentication. Change `Server` if SQL Server runs elsewhere. |
| `Authentication:Auth_type` | `TokenBased` or `Existing` | `Existing` makes login return 501. |
| `Authentication:Jwt:Issuer` | `NeetBee` | Must match the gateway. |
| `Authentication:Jwt:Audience` | `NeetBeeClients` | Must match the gateway. |
| `Authentication:Jwt:SigningKey` | leave empty | Supply through the environment variable above. |
| `Authentication:Jwt:AccessTokenExpiryMinutes` | `15` | Allowed range 1 to 60. |
| `Authentication:Jwt:RefreshTokenExpiryDays` | `30` | Allowed range 1 to 90. |

Swagger (Development only): http://localhost:5001/swagger

## 5. ApiGateway

File: `ApiGateway\appsettings.json`

| Key | Value | Notes |
|---|---|---|
| `Kestrel:Endpoints:Http:Url` | `http://localhost:5000` | Public entry point. |
| `Cors:AllowedOrigins` | `http://localhost:5100`, `http://127.0.0.1:5100`, plus `5500` for a separate dev server | Add the origin of any other front end here. Use HTTPS origins in production. |
| `Authentication:Auth_type` | `TokenBased` or `Existing` | Must match the auth service. |
| `Authentication:Jwt:Issuer` | `NeetBee` | Must match the auth service. |
| `Authentication:Jwt:Audience` | `NeetBeeClients` | Must match the auth service. |
| `Authentication:Jwt:SigningKey` | leave empty | Supply through the environment variable. |
| `ReverseProxy:Routes:auth-me` | `/api/auth/me`, policy `Default` | Requires a valid JWT. |
| `ReverseProxy:Routes:auth-public` | `/api/auth/{**catch-all}`, policy `Anonymous` | Register, login, refresh, logout. |
| `ReverseProxy:Clusters:authentication:Destinations:primary:Address` | `http://localhost:5001/` | Address of the auth service. |
| Rate limit | 100 requests per minute per client IP | Set in `ApiGateway\Program.cs`. |

To add a downstream service later, add a cluster and a route here. Routes are the only way traffic reaches a service.

## 6. WebApplication (web app)

File: `WebApplication\appsettings.json`

| Key | Value | Notes |
|---|---|---|
| `Kestrel:Endpoints:Http:Url` | `http://localhost:5100` | Open this in a browser. |
| `Site:RootPath` | `..\..` | Folder that holds `index.html`, `css`, `js`, `assets`, `login`. Relative to `WebApplication`. |
| `Gateway:BaseUrl` | `http://localhost:5000` | ApiGateway address. The web server calls it for login, register, refresh and logout. |
| `Session:SecureCookies` | `false` | Set to `true` in production (HTTPS only) so the session cookie is sent only over HTTPS. |

Pages:

| Route | Layout | Access |
|---|---|---|
| `/` and the static site pages | Static site | Public |
| `/Account/Login` | Public (header with Login and Sign Up, footer) | Anonymous |
| `/Account/Register` | Public | Anonymous |
| `/Dashboard` | Dashboard (header with name and Log out, sidebar, footer) | Signed in |
| `POST /Account/Logout` | none (redirects to login) | Signed in |

The session is stored in the encrypted `neetbee.session` cookie (HttpOnly). It holds the access token and refresh token. The web server refreshes the access token about one minute before it expires.

The web app serves only: top-level `*.html` pages, and the `css`, `js`, `assets` and `login` folders. It does not serve backend code, configuration files, or `Other HTML Templages`.

The login page calls the gateway at `http://localhost:5000` (set in `login\index.html`, `API_BASE`). If you change the gateway port, change that value too.

## 7. Opening and running

Open the solution in Visual Studio 2022 (17.8 or later) or VS Code, or run the helper script:

```powershell
cd "D:\Website\NEET_BEE_App_v1.0\backend"
.\run-all.ps1 -SigningKey "<your 32+ character random key>"
```

The script starts the three projects in separate windows, in this order: AuthenticationService, ApiGateway, WebApplication.

To run from Visual Studio, set **Multiple startup projects** (right-click the solution, Properties) to the three projects with action **Start**.

Manual commands:

```powershell
dotnet build "D:\Website\NEET_BEE_App_v1.0\backend\NeetBee.sln"
dotnet run --project "D:\Website\NEET_BEE_App_v1.0\backend\AuthenticationService"
dotnet run --project "D:\Website\NEET_BEE_App_v1.0\backend\ApiGateway"
dotnet run --project "D:\Website\NEET_BEE_App_v1.0\backend\WebApplication"
```

## 8. Checks

| URL | Expect |
|---|---|
| http://localhost:5100/ | NEET BEE home page |
| http://localhost:5100/login/ | Sign-in page |
| http://localhost:5100/health | `{"status":"ok"}` |
| http://localhost:5001/swagger | Swagger UI (Development) |
| `POST http://localhost:5000/api/auth/register` | 201 |
| `GET http://localhost:5000/api/auth/me` without a token | 401 |

## 9. Production notes

- Use HTTPS for every service and put the gateway behind a real host name.
- Store the signing key in a secret store such as Azure Key Vault, not an environment variable on a shared machine.
- Set `ASPNETCORE_ENVIRONMENT=Production`; Swagger is then disabled.
- Replace the SQL Server connection with a service account, or use the connection string from the secret store.
- Restrict CORS to the production origin only.
- Use a stricter rate limit on login than the global limit.

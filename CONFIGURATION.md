# NEET BEE: configuration reference

Solution: `NeetBee.sln` at the repository root.

Folder layout (each folder is also a solution folder in Visual Studio):

| Folder | Projects |
|---|---|
| `Web\` | `WebApplication` (the website) |
| `Mobile\` | `MobileApp` (`NeetBee.Mobile`, Android and iOS app) |
| `Services\` | `ApiGateway`, `AuthenticationService`, `NotificationService`, `PaymentService` |
| `Common\` | `NeetBee.Common`: shared logging setup (`AddNeetBeeLogging`) and the NotificationService contract and client (`NotificationType`, `NotificationChannel`, `NotificationData`, `INotificationClient`), used by the services |

| Project | URL | Depends on |
|---|---|---|
| WebApplication (web app) | http://localhost:5100 | Gateway (called from the browser) |
| ApiGateway | http://localhost:5000 | AuthenticationService, PaymentService, NotificationService |
| AuthenticationService | http://localhost:5001 | SQL Server (`NEET_BEE`), NotificationService (sign-in code emails). Listens on host interfaces for the Dockerized gateway; restrict access with the host firewall. |
| NotificationService | http://localhost:5002 | SMTP server, SMS / WhatsApp provider. Listens on host interfaces for the Dockerized gateway; restrict access with the host firewall. |
| PaymentService | http://localhost:5003 | SQL Server (`NEET_BEE`), payment gateway, NotificationService. Listens on host interfaces for the Dockerized gateway; restrict access with the host firewall. |
| SQL Server | localhost, Windows Authentication | none |

Start order: SQL Server, then NotificationService, then AuthenticationService and PaymentService, then ApiGateway, then WebApplication.

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
| Tables | `dbo.Users`, `dbo.RefreshTokens`; scripts `002` to `008` add the rest (run them in order) |

## 3. Environment variables (secrets)

| Variable | Used by | Required | Value |
|---|---|---|---|
| `Authentication__Jwt__SigningKey` | AuthenticationService, PaymentService and ApiGateway | Yes (TokenBased mode) | Random string, 32+ characters. All three must have the same value. |
| `NotificationApi__ApiKey` | NotificationService | Yes outside Development | Random string, 32+ characters. Callers send it in `X-Api-Key`. Empty in Development = calls accepted without a key. |
| `NotificationService__ApiKey` | AuthenticationService and PaymentService | Same as above | The same value as `NotificationApi__ApiKey`. |
| `Email__Password` | NotificationService | When SMTP needs a password | SMTP password. |
| `Messaging__AuthToken` | NotificationService | When `Messaging:Provider` is `Twilio` | Twilio auth token. |
| `Payments__Razorpay__KeySecret`, `Payments__Razorpay__WebhookSecret` | PaymentService | Not yet (no Razorpay gateway yet) | Razorpay secrets. |

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

File: `Services\AuthenticationService\appsettings.json`

| Key | Value | Notes |
|---|---|---|
| `Kestrel:Endpoints:Http:Url` | `http://0.0.0.0:5001` | Listens on host interfaces so the Dockerized gateway can reach the service. Keep port 5001 blocked from external networks with the host firewall. |
| `ConnectionStrings:NeetBee` | `Server=localhost;Database=NEET_BEE;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;` | Windows Authentication. Change `Server` if SQL Server runs elsewhere. |
| `Authentication:Auth_type` | `TokenBased` or `Existing` | `Existing` makes login return 501. |
| `Authentication:Jwt:Issuer` | `NeetBee` | Must match the gateway. |
| `Authentication:Jwt:Audience` | `NeetBeeClients` | Must match the gateway. |
| `Authentication:Jwt:SigningKey` | leave empty | Supply through the environment variable above. |
| `Authentication:Jwt:AccessTokenExpiryMinutes` | `15` | Allowed range 1 to 60. |
| `Authentication:Jwt:RefreshTokenExpiryDays` | `30` | Allowed range 1 to 90. |
| `NotificationService:BaseUrl` | `http://localhost:5002/` | Sign-in code emails are sent through NotificationService. |
| `NotificationService:ApiKey` | leave empty | Supply through `NotificationService__ApiKey`. |

Swagger (Development only): http://localhost:5001/swagger

## 4a. NotificationService

File: `Services\NotificationService\appsettings.json`. Endpoint: `POST api/notifications` with
`{ "channel": "Email" | "Sms" | "WhatsApp", "type": "Otp" | "EnrollmentSuccess" | "PaymentSuccess" | "PaymentFailed" | "LearningStatusUpdate", "recipient": "...", "data": { ... } }`.
Returns 202 when sent, 400 when a template value is missing, 401 without the key, 502 when delivery failed.
Message text lives in `Services\NotificationService\Notifications\NotificationTemplates.cs`.

| Key | Value | Notes |
|---|---|---|
| `Kestrel:Endpoints:Http:Url` | `http://0.0.0.0:5002` | Listens on host interfaces so the Dockerized gateway can reach the service. Keep port 5002 blocked from external networks with the host firewall. |
| `NotificationApi:ApiKey` | leave empty | Supply through `NotificationApi__ApiKey`. |
| `Email:Host`, `Email:Port`, `Email:UseSsl`, `Email:UserName`, `Email:FromAddress`, `Email:FromName` | empty / `587` / `true` | SMTP settings (moved here from AuthenticationService). Empty `Host` in Development logs the message instead of sending it. |
| `Messaging:Provider` | `Stub` or `Twilio` | `Stub` sends nothing (Development logs the text). |
| `Messaging:AccountSid`, `Messaging:SmsFrom`, `Messaging:WhatsAppFrom` | empty | Twilio account and sender numbers (E.164). |

## 4b. PaymentService

File: `Services\PaymentService\appsettings.json`. Endpoints (JWT required): `POST api/payments/netbanking`, `POST api/payments/upi`,
`POST api/payments/card`, `GET api/payments/history`. Table: `dbo.PaymentTransactions` (`Database\008_create_payment_transactions.sql`).

| Key | Value | Notes |
|---|---|---|
| `Kestrel:Endpoints:Http:Url` | `http://0.0.0.0:5003` | Listens on host interfaces so the Dockerized gateway can reach the service. Keep port 5003 blocked from external networks with the host firewall. |
| `ConnectionStrings:NeetBee` | same as AuthenticationService | |
| `Authentication:Jwt:Issuer` / `Audience` / `SigningKey` | `NeetBee` / `NeetBeeClients` / empty | Must match the auth service. |
| `Payments:Gateway` | `Fake` | Only `Fake` exists (no money moves). Test failures: bank `FAIL`, UPI `fail@...`, card token `tok_fail`. |
| `Payments:Currency` / `Payments:MaxAmount` | `INR` / `100000` | |
| `Payments:Razorpay:KeyId` | empty | For a future Razorpay gateway. |
| `NotificationService:BaseUrl` / `ApiKey` | `http://localhost:5002/` / empty | Payment success / failed messages. |

## 5. ApiGateway

File: `Services\ApiGateway\appsettings.json`

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
| `ReverseProxy:Routes:payments` | `/api/payments/{**catch-all}`, policy `Default` | PaymentService. Requires a valid JWT. |
| `ReverseProxy:Routes:notifications` | `/api/notifications/{**catch-all}`, policy `Default` | NotificationService. Requires a valid JWT and the `X-Api-Key`. |
| `ReverseProxy:Clusters:authentication:Destinations:primary:Address` | `http://localhost:5001/` locally; `http://192.168.65.254:5001/` in Docker Desktop | Address of the auth service. The Dockerfile sets the Docker value for this Docker Desktop host. |
| `ReverseProxy:Clusters:notification:Destinations:primary:Address` | `http://localhost:5002/` locally; `http://192.168.65.254:5002/` in Docker Desktop | Address of the notification service. The Dockerfile sets the Docker value for this Docker Desktop host. |
| `ReverseProxy:Clusters:payment:Destinations:primary:Address` | `http://localhost:5003/` locally; `http://192.168.65.254:5003/` in Docker Desktop | Address of the payment service. The Dockerfile sets the Docker value for this Docker Desktop host. |
| Rate limit | 100 requests per minute per client IP | Set in `Services\ApiGateway\Program.cs`. |

To add a downstream service later, add a cluster and a route here. Routes are the only way traffic reaches a service.

## 6. WebApplication (web app)

File: `Web\WebApplication\appsettings.json`

| Key | Value | Notes |
|---|---|---|
| `Kestrel:Endpoints:Http:Url` | `http://localhost:5100` | Open this in a browser. |
| `Site:RootPath` | `.` | Folder that holds `index.html`, `css`, `js`, `assets`, `login`. Relative to `Web\WebApplication`. |
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
cd "D:\Github\NEET-Bee\NeetBee-DEV"
.\run-all.ps1 -SigningKey "<your 32+ character random key>"
```

The script starts the five projects in separate windows, in this order: NotificationService, AuthenticationService, PaymentService, ApiGateway, WebApplication.

To run from Visual Studio, set **Multiple startup projects** (right-click the solution, Properties) to the five projects above with action **Start**.

Manual commands:

```powershell
cd "D:\Github\NEET-Bee\NeetBee-DEV"
dotnet build NeetBee.sln
dotnet run --project Services\NotificationService
dotnet run --project Services\AuthenticationService
dotnet run --project Services\PaymentService
dotnet run --project Services\ApiGateway
dotnet run --project Web\WebApplication
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

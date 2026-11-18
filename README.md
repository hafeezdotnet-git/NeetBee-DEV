# NEET BEE backend, test

Two ASP.NET Core 8 services:

| Project | Port | Role |
|---|---|---|
| `ApiGateway` | `http://localhost:5000` | YARP reverse proxy. Validates JWTs, rate limits, CORS, correlation IDs. The only public entry point. |
| `AuthenticationService` | `http://localhost:5001` | Register, login, refresh (rotation and reuse detection), logout. Talks to SQL Server. Bound to localhost only. |

## 1. Create the database (SQL Server, Windows Authentication)

From a terminal on the machine running SQL Server:

```powershell
sqlcmd -S localhost -E -i "D:\Website\NEET_BEE_App_v1.0\backend\Database\001_create_neet_bee.sql"
```

This creates the `NEET_BEE` database and the `Users` and `RefreshTokens` tables. It is safe to re-run.

## 2. Set the signing key (never commit it)

Use a random string of at least 32 characters. Set it for the current PowerShell session, then start both services from that same session:

```powershell
$env:Authentication__Jwt__SigningKey = [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
```

To persist it for the user account instead, use `setx Authentication__Jwt__SigningKey "<value>"` and open new terminals afterwards. Both services must use the same value.

## 3. Run

```powershell
dotnet run --project "D:\Website\NEET_BEE_App_v1.0\backend\AuthenticationService"
dotnet run --project "D:\Website\NEET_BEE_App_v1.0\backend\ApiGateway"
```

Swagger for the auth service is at `http://localhost:5001/swagger` in Development.

## 4. Endpoints (via the gateway, `http://localhost:5000`)

| Method | Path | Auth | Body | Success |
|---|---|---|---|---|
| POST | `/api/auth/register` | none | `{ "userName", "email", "password" }` | 201 `{ "id" }` |
| POST | `/api/auth/login` | none | `{ "username", "password" }` (username or email) | 200 tokens |
| POST | `/api/auth/refresh` | none | `{ "refreshToken" }` | 200 new tokens |
| POST | `/api/auth/logout` | none | `{ "refreshToken" }` | 204 |
| GET | `/api/auth/me` | `Bearer` access token | none | 200 `{ id, userName }` |

Token response:

```json
{ "accessToken": "...", "refreshToken": "...", "tokenType": "Bearer", "expiresIn": 900 }
```

Failures return RFC 7807 ProblemDetails. Refresh failures return 401. Reuse of a rotated refresh token revokes its whole family.

## 5. Try it

```powershell
curl.exe -s -X POST http://localhost:5000/api/auth/register -H "Content-Type: application/json" -d "{\"userName\":\"student1\",\"email\":\"student1@example.com\",\"password\":\"Passw0rd!\"}"
curl.exe -s -X POST http://localhost:5000/api/auth/login -H "Content-Type: application/json" -d "{\"username\":\"student1\",\"password\":\"Passw0rd!\"}"
curl.exe -s http://localhost:5000/api/auth/me -H "Authorization: Bearer <accessToken>"
```

## Authentication modes

`Authentication:Auth_type` in `appsettings.json` (both services):

- `TokenBased`: JWT access tokens (15 min) and rotating refresh tokens (30 days). Gateway requires a valid JWT on `/api/auth/me` and on any future protected route.
- `Existing`: the gateway allows anonymous access. The auth service's login endpoints return 501, because this codebase has no existing login to preserve. See the open questions below.

## Notes and known gaps

- Tests are not written yet (unit and integration tests are required by the spec).
- Solution file: `backend\NeetBee.sln` contains all three projects. See `CONFIGURATION.md` for every setting.
- Database schema is created by the SQL script, not EF migrations. Changing the model means changing the script.
- Refresh tokens are returned in the JSON body. The spec leaves browser storage open. An HttpOnly cookie is the safer choice and needs a decision.
- The gateway's rate limit is global (100 requests per minute per IP). A stricter limit on login is recommended.

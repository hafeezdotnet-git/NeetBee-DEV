# Docker

The Compose setup builds the WebApplication, AuthenticationService, NotificationService, PaymentService, and ApiGateway images. The services share a private Compose network; the web app and gateway publish host ports.

## Configure

From the repository root, copy the example environment file and edit it:

```powershell
Copy-Item .env.example .env
notepad .env
```

Set `AUTHENTICATION_JWT_SIGNING_KEY` and `NOTIFICATION_API_KEY` to separate random secrets of at least 32 characters. Set `SA_PASSWORD` to the SQL Server SA password. Compose uses it both to start the SQL Server container and to configure AuthenticationService, PaymentService, and WebApplication to connect to that container over the private Compose network.

## Build and run

If you already have a standalone gateway container publishing port 5000, stop it before starting the Compose gateway:

```powershell
docker stop apigateway
```

```powershell
docker compose build web authentication notification payment gateway
docker compose up -d
```

Or build and start everything in one command:

```powershell
docker compose up --build -d
```

The Swagger UI is available at <http://localhost:5000/swagger>, and the website at <http://localhost:5100>. Check container status and logs with:

```powershell
docker compose ps
docker compose logs -f
```

Stop the stack with `docker compose down`. The SQL Server data volume is retained.

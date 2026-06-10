# VxPayBridge

A .NET 9 payment gateway orchestrator that acts as a single point of integration between internal applications (Auction, Inventory, etc.) and external payment providers (Paystack, with Hubtel/Stripe extensibility).

## Architecture

- **Vertical Slice Architecture** using MediatR + Carter
- **Clean abstraction** via `IPaymentProvider` — swap or add providers without touching business logic
- **Idempotent** payment initialisation and webhook processing
- **Reliable webhook delivery** via Hangfire background jobs with automatic retry
- **PostgreSQL** for persistence via EF Core

## Getting Started

### Prerequisites
- .NET 9 SDK
- PostgreSQL (running locally or via Docker)
- A Paystack account with a secret key

### Setup

1. **Copy the example config and fill in your values:**
   ```bash
   cp VxPayBridge.API/appsettings.Example.json VxPayBridge.API/appsettings.json
   ```
   Then edit `appsettings.json` with your real database credentials, Paystack secret key, and internal API key.

2. **Apply database migrations:**
   ```bash
   cd VxPayBridge.API
   dotnet run -- --migrate
   ```

3. **Run the API:**
   ```bash
   dotnet run
   ```
   Swagger UI is available at: `http://localhost:5000/swagger`

### Register a Client App

```http
POST /api/internal/clients
x-internal-api-key: your-internal-api-key

{
  "code": "auction",
  "name": "Auction System",
  "webhookUrl": "https://your-auction-app.com/webhooks/payments"
}
```

> Save the returned `clientId` and `clientSecret` — the secret is only shown once.

---

## API Reference

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/api/internal/clients` | `x-internal-api-key` | Register a new client app |
| `POST` | `/api/payments/initialize` | `x-client-id` + `x-client-secret` | Initialize a payment |
| `GET`  | `/api/payments/status/{clientReference}` | `x-client-id` + `x-client-secret` | Poll payment status |
| `POST` | `/api/webhooks/paystack` | Paystack HMAC signature | Receive Paystack events |
| `GET`  | `/hangfire` | — | Background job dashboard |

## Adding a New Payment Provider

1. Implement `IPaymentProvider` in `SharedServices/Providers/`
2. Register the new implementation in `Program.cs`

That's it — no other changes needed.

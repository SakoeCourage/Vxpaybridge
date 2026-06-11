# VxPayBridge — Payment Integration & State Orchestration

VxPayBridge is a stateful orchestrator, ledger, and payment gateway bridge designed to interface securely between Variable X Solutions internal applications (e.g. `ADBAuction`, Inventory Management) and **Paystack**. It ensures secure, transaction-safe, and idempotent payment processing without exposing core credentials or Paystack logic to frontend or multiple distinct backend systems.

## Base URL
All production endpoints are hosted at:
`https://vxpaybridge.fly.dev`

---

## High-Level Architecture

```mermaid
sequenceDiagram
    autonumber
    participant ClientApp as Internal Client App (e.g., ADBAuction)
    participant Bridge as VxPayBridge (https://vxpaybridge.fly.dev)
    participant Paystack as Paystack API
    participant Customer as Customer Browser

    %% Client Registration
    Note over ClientApp, Bridge: 0. Registration (Internal Only)
    ClientApp->>Bridge: Register App (admin bearer token required)
    Bridge-->>ClientApp: Return ClientId & ClientSecret

    %% Payment Initialization Flow
    Note over ClientApp, Bridge: 1. Payment Initialization Flow
    ClientApp->>Bridge: POST /api/payments/initialize<br/>(Sign with ClientId + ClientSecret headers)
    Bridge->>Bridge: Idempotency check & DB draft creation
    Bridge->>Paystack: Initialize Transaction API
    Paystack-->>Bridge: Returns Authorization URL & Access Code
    Bridge->>Bridge: Update DB status to PENDING
    Bridge-->>ClientApp: Returns Authorization URL & Access Code
    ClientApp-->>Customer: Redirect user to Paystack Payment Page

    %% Payment Verification Flow (Webhook)
    Note over Customer, Paystack: 2. Customer pays on Paystack
    Customer->>Paystack: Completes Payment
    Paystack->>Bridge: POST /api/webhooks/paystack (Webhook Event)
    Bridge->>Bridge: Validate Signature & Deduplicate Event
    Bridge->>Bridge: Save Webhook Event & Schedule Delivery
    Bridge-->>Paystack: 200 OK (Immediately acknowledge)

    %% Webhook Retry / Delivery
    Note over Bridge, ClientApp: 3. Background Webhook Delivery
    Bridge->>ClientApp: POST Webhook Delivery (HMAC signed payload)
    ClientApp-->>Bridge: 200 OK
    Bridge->>Bridge: Mark Webhook Event as SUCCESS
```

---

## 🔒 Authentication Security

VxPayBridge uses two tiers of authentication:

1. **Internal Admin Endpoints** (`/api/internal/*`)
   * Protected by user login + SMS OTP.
   * Header required after OTP verification: `Authorization: Bearer <access_token>`

2. **Client Endpoints** (`/api/payments/*`)
   * Protected by application-specific credentials generated during client registration.
   * Headers required:
     * `x-client-id: <client_id>`
     * `x-client-secret: <client_secret>`

---

## 🚀 API Endpoint Documentation

### 1. Login and Verify OTP
Internal users log in with username, email, or telephone number plus password. VxPayBridge sends an OTP to the user's telephone number through Arkesel SMS. The user must verify the OTP before receiving an access token.

* **Endpoint**: `POST /api/auth/login`
* **Request Payload**:
  ```json
  {
    "login": "admin@example.com",
    "password": "StrongPassword123"
  }
  ```
* **Response Payload (200 OK)**:
  ```json
  {
    "otpRequired": true,
    "message": "OTP sent to the user's telephone number"
  }
  ```

* **Endpoint**: `POST /api/auth/verify-otp`
* **Request Payload**:
  ```json
  {
    "login": "admin@example.com",
    "otp": "123456"
  }
  ```
* **Response Payload (200 OK)**:
  ```json
  {
    "accessToken": "<token>",
    "expiresAt": "2026-06-11T19:30:00Z"
  }
  ```

The initial internal user is seeded through database migrations. Additional users must be created by an authenticated internal user through `POST /api/internal/users`.

### 2. Register Client Application (Internal Admin Only)
Registers a new internal application (like `ADBAuction`) and provides client credentials.

* **Endpoint**: `POST /api/internal/clients`
* **Headers**:
  * `Authorization`: `Bearer <access_token>`
* **Request Payload**:
  ```json
  {
    "code": "ADBAuction",
    "name": "ADB Auction Portal",
    "webhookUrl": "https://www.vxauction.store/api/payments/webhook"
  }
  ```
* **Response Payload (200 OK)**:
  ```json
  {
    "clientId": "client_8b99d52a220b4ea78e47f2e1a384e912",
    "clientSecret": "sec_8e72ba6f3162a8c3d98fb69a7c36a4b1..."
  }
  ```
  > [!WARNING]
  > The `clientSecret` is **only returned once** during this creation step. Store it securely. VxPayBridge stores a hash for client authentication and keeps a server-side signing copy for webhook delivery signatures.

### 3. Activate or Deactivate Client Application
Marks a client app active or inactive. Inactive client apps cannot call protected client endpoints with `x-client-id` and `x-client-secret`.

* **Endpoint**: `PATCH /api/internal/clients/{id}/status`
* **Headers**:
  * `Authorization`: `Bearer <access_token>`
* **Request Payload**:
  ```json
  {
    "isActive": false
  }
  ```

### 4. Create Internal User
Creates another user who can log in with password + SMS OTP and manage internal endpoints.

* **Endpoint**: `POST /api/internal/users`
* **Headers**:
  * `Authorization`: `Bearer <access_token>`
* **Request Payload**:
  ```json
  {
    "userName": "ops-admin",
    "email": "ops@example.com",
    "telephoneNumber": "0244111111",
    "password": "StrongPassword123"
  }
  ```

Internal users can also be listed without exposing password hashes or raw passwords.

* **Endpoint**: `GET /api/internal/users`
* **Headers**:
  * `Authorization`: `Bearer <access_token>`
* **Response Payload (200 OK)**:
  ```json
  [
    {
      "id": "45db66dc-f9ef-47e3-bf08-751d946c07ab",
      "userName": "Sakoe Courage",
      "email": "akorlicourage@gail.com",
      "telephoneNumber": "0203843143",
      "isActive": true
    }
  ]
  ```

---

### 5. Initialize Payment
Initiates a new transaction with Paystack and returns the checkout URL.

* **Endpoint**: `POST /api/payments/initialize`
* **Headers**:
  * `x-client-id`: `<your_client_id>`
  * `x-client-secret`: `<your_client_secret>`
* **Request Payload**:
  ```json
  {
    "amount": 250.00,
    "currency": "GHS",
    "clientReference": "LOT-000123",
    "clientEmail": "customer@example.com",
    "callbackUrl": "https://www.vxauction.store/payment/callback",
    "audType": "seller",
    "audId": "seller_456",
    "metadata": {
      "lotId": "lot_001",
      "lotNumber": "LOT-000123",
      "paymentId": "payment_001",
      "buyerId": "buyer_001"
    }
  }
  ```
* **Response Payload (200 OK)**:
  ```json
  {
    "authorizationUrl": "https://checkout.paystack.com/qy812a...",
    "accessCode": "qy812a...",
    "reference": "TRX-7b9954ab727ed1079516470b820ece66"
  }
  ```

#### 🛡️ Idempotency Guarantee
* The `clientReference` combined with the calling application identity acts as a unique idempotency key.
* The `clientReference` must be generated and stored by the client application. Reuse it when retrying the same payment or withdrawal. Generate a new one only for a new money action.
* For Inventory payment collection, the order number is a good `clientReference` if one order maps to one payment.
* For Auction sale payment collection, the unique lot number can be used as the `clientReference` if one lot maps to one sale payment.
* The `audType` and `audId` identify the ledger owner inside the authenticated client application. For example, Auction may use `seller/seller_456`, while Inventory may use `tenant/tenant_123`.
* If a payment initialization request is repeated with the **same** `clientReference` while the previous transaction is still `PENDING`, VxPayBridge will bypass calling Paystack again and instantly return the existing `authorizationUrl` and `accessCode`.
* If the transaction has already succeeded or failed, the system blocks the request to prevent double-charges.

---

### 6. Check Payment Status
Allows client applications to pull the current status of a payment transaction at any time.

* **Endpoint**: `GET /api/payments/status/{clientReference}`
* **Headers**:
  * `x-client-id`: `<your_client_id>`
  * `x-client-secret`: `<your_client_secret>`
* **Response Payload (200 OK)**:
  ```json
  {
    "id": "7b9954ab-727e-d107-9516-470b820ece66",
    "clientReference": "auction-sale-9843",
    "gatewayTransactionId": "TRX-7b9954ab727ed1079516470b820ece66",
    "audType": "user",
    "audId": "seller_456",
    "amount": 250.00,
    "currency": "GHS",
    "status": "SUCCESS",
    "createdAt": "2026-06-10T17:39:31Z",
    "updatedAt": "2026-06-10T17:39:32Z"
  }
  ```
  > Valid Status values: `INITIALIZING`, `PENDING`, `SUCCESS`, `FAILED`.

---

### 7. Fetch Paystack Banks
Returns Ghana bank options that can be used for Paystack bank-related flows.

* **Endpoint**: `GET /api/payments/banks`
* **Headers**:
  * `x-client-id`: `<client_id>`
  * `x-client-secret`: `<client_secret>`
* **Response Payload (200 OK)**:
  ```json
  {
    "status": true,
    "data": [
      {
        "name": "Example Bank",
        "code": "123"
      }
    ]
  }
  ```

---

### 8. Fetch Mobile Money Providers
Returns Ghana mobile money provider options supported by Paystack.

* **Endpoint**: `GET /api/payments/mobile-money-providers`
* **Headers**:
  * `x-client-id`: `<client_id>`
  * `x-client-secret`: `<client_secret>`
* **Response Payload (200 OK)**:
  ```json
  {
    "status": true,
    "data": [
      {
        "name": "MTN",
        "code": "mtn"
      }
    ]
  }
  ```

---

### 9. Resolve Account
Resolves a bank or mobile money account number against a Paystack provider code.

* **Endpoint**: `GET /api/payments/resolve-account?code=<provider_code>&accountNumber=<account_number>`
* **Headers**:
  * `x-client-id`: `<client_id>`
  * `x-client-secret`: `<client_secret>`
* **Response Payload (200 OK)**:
  ```json
  {
    "status": true,
    "data": {
      "accountName": "Customer Name",
      "accountNumber": "0244000000"
    }
  }
  ```

---

### 10. Get Ledger Balance
Returns the current VxPayBridge balance for an entity inside the authenticated client application.

* **Endpoint**: `GET /api/ledger/balance?audType=<aud_type>&audId=<aud_id>&currency=GHS`
* **Headers**:
  * `x-client-id`: `<your_client_id>`
  * `x-client-secret`: `<your_client_secret>`
* **Response Payload (200 OK)**:
  ```json
  {
    "audType": "user",
    "audId": "seller_456",
    "currency": "GHS",
    "availableBalance": 150.00,
    "pendingBalance": 100.00,
    "totalBalance": 250.00
  }
  ```

---

### 11. Get Ledger Transactions
Returns paginated ledger entries for an entity inside the authenticated client application.

* **Endpoint**: `GET /api/ledger/transactions?audType=<aud_type>&audId=<aud_id>&currency=GHS&pageNumber=1&pageSize=20`
* **Headers**:
  * `x-client-id`: `<your_client_id>`
  * `x-client-secret`: `<your_client_secret>`
* **Response Payload (200 OK)**:
  ```json
  {
    "totalCount": 1,
    "pageNumber": 1,
    "pageSize": 20,
    "data": [
      {
        "type": "CREDIT",
        "reason": "PAYMENT_SUCCESS",
        "reference": "payment:7b9954ab-727e-d107-9516-470b820ece66",
        "amount": 250.00,
        "currency": "GHS",
        "balanceAfter": 250.00,
        "createdAt": "2026-06-10T17:39:32Z"
      }
    ]
  }
  ```

---

### 12. Confirm Withdrawal
Initiates a Paystack transfer from the VxPayBridge ledger balance for a specific `audType` + `audId`.

* **Endpoint**: `POST /api/payments/withdrawal/confirm`
* **Headers**:
  * `x-client-id`: `<your_client_id>`
  * `x-client-secret`: `<your_client_secret>`
* **Request Payload**:
  ```json
  {
    "amount": 100.00,
    "currency": "GHS",
    "audType": "seller",
    "audId": "seller_456",
    "code": "mtn",
    "accountNumber": "0244000000",
    "accountName": "Customer Name",
    "clientReference": "LOT-000123-payout",
    "reason": "Seller payout for lot LOT-000123"
  }
  ```
* **Response Payload (200 OK)**:
  ```json
  {
    "success": true,
    "status": "QUEUED",
    "transferCode": null
  }
  ```

VxPayBridge reserves the amount from `availableBalance` into `pendingBalance`, stores the withdrawal as `QUEUED`, and processes the Paystack transfer asynchronously. If Paystack transfer initiation fails, the reserved amount is reversed back into available balance. If Paystack confirms transfer success, the pending amount is cleared.

The withdrawal `clientReference` must be different from the payment collection `clientReference`. For example, Auction may use `LOT-000123` for the buyer payment and `LOT-000123-payout` for the seller payout.

> Valid withdrawal status values: `QUEUED`, `PROCESSING`, `PENDING`, `SUCCESS`, `FAILED`, `REVERSED`.

---

### 13. Paystack Webhook Handler (Public)
Public-facing endpoint target configured in your Paystack dashboard to receive real-time webhook updates.

* **Endpoint**: `POST /api/webhooks/paystack`
* **Webhook Target URL**: `https://vxpaybridge.fly.dev/api/webhooks/paystack`
* **Payload signature verification**: Verified using the `x-paystack-signature` header as an HMAC SHA-512 signature against the system's `Paystack:SecretKey`.
* **Webhook Deduplication**: Webhooks are uniquely deduplicated using the combination of `gateway_transaction_id` + `event` to ensure that duplicate webhook calls from Paystack do not trigger duplicate handlers or deliveries.
* **Ledger Updates**: `charge.success` credits the matching ledger account. `transfer.failed` and `transfer.reversed` reverse reserved withdrawal amounts.
* **Recovery Jobs**: Stored webhook events with `PENDING` or `FAILED` delivery status are periodically re-enqueued for delivery. Queued withdrawals are also periodically retried by the background worker.

---

## ⚡ Reliable Webhook Delivery

To guarantee that your client application is notified of payment events even during network interruptions:
1. When a webhook is verified, VxPayBridge saves the event and immediately returns a `200 OK` to Paystack.
2. An asynchronous background worker is scheduled to forward the webhook payload to the client app's registered `webhookUrl`.
3. The delivery payload sent to the client application is signed using an HMAC SHA-256 signature passed in the `x-payload-signature` header.
4. **Automatic Retries**: If your application is down or returns a non-2xx status code, the bridge will automatically retry delivering the notification with exponential backoff.

### Webhook Signature Verification in Client Apps
When your application receives a webhook delivery from VxPayBridge, you should verify the signature to ensure it came from the bridge:
1. Compute the HMAC SHA-256 signature of the raw request body bytes using your application's `clientSecret` as the key.
2. Convert the computed hash to a hexadecimal string.
3. Compare the computed hash with the signature provided in the `x-payload-signature` header (using a constant-time comparison).

#### Code Examples

##### C# (.NET)
```csharp
using System.Security.Cryptography;
using System.Text;

public static bool VerifySignature(string rawRequestBody, string clientSecret, string receivedSignature)
{
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(clientSecret));
    var computedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawRequestBody));
    var computedSignature = Convert.ToHexString(computedBytes).ToLowerInvariant();
    
    // Constant-time comparison to prevent timing attacks
    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(computedSignature), 
        Encoding.UTF8.GetBytes(receivedSignature.ToLowerInvariant())
    );
}
```

##### Node.js
```javascript
const crypto = require('crypto');

function verifySignature(rawRequestBody, clientSecret, receivedSignature) {
    const computedSignature = crypto
        .createHmac('sha256', clientSecret)
        .update(rawRequestBody)
        .digest('hex');
        
    return crypto.timingSafeEqual(
        Buffer.from(computedSignature, 'utf-8'),
        Buffer.from(receivedSignature, 'utf-8')
    );
}
```

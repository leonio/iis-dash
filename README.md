# iis-dash Monorepo

Vue 3.5 + Vite + Tailwind v4 + shadcn-vue frontend with a .NET 10 Minimal API backend using NTLM/Windows authentication.

## Stack
- **Frontend:** Vue 3.5, Vite, Tailwind CSS v4 (CSS-first), shadcn-vue, Unovis
- **Backend:** .NET 10 Minimal API, Microsoft.AspNetCore.Authentication.Negotiate
- **Auth:** NTLM / Windows Authentication
- **CORS:** `http://localhost:5173` with credentials

## Prerequisites
- **Node.js** (latest LTS)
- **pnpm**
- **.NET SDK 10** (preview if needed)
- Windows environment for NTLM testing

## Setup
```bash
# install pnpm if needed
npm install -g pnpm

# frontend
cd client
pnpm install

# backend
cd ..\server
dotnet restore
```

## Run
```bash
# frontend
cd client
pnpm dev

# backend
cd ..\server
dotnet run
```

Frontend: http://localhost:5173
Backend: http://localhost:5039

## Test log generation
Generate sample IIS log files using the single-file C# script:

```bash
# defaults to W3C
dotnet run --project gen-logdata.cs

# IIS Log File Format (CSV)
dotnet run --project gen-logdata.cs iis
```

This writes `iis-w3c.log` or `iis-csv.log` to the current directory.

## Endpoints
- `GET /api/hello` → returns `User.Identity.Name` (requires NTLM)
- `POST /api/logs/precheck` → multipart upload; reads first line and detects format
- `POST /api/logs/upload` → multipart upload; ingests W3C or IIS Log File Format

## Frontend notes
- Tailwind v4 is configured in `src/app.css` using CSS-first `@theme`.
- shadcn-vue components live under `src/components/ui`.
- API calls use `credentials: 'include'` in `src/composables/useApi.ts` to pass NTLM tokens.

## Backend notes
- NTLM/Windows auth is enabled via `Microsoft.AspNetCore.Authentication.Negotiate`.
- CORS allows `http://localhost:5173` with credentials.
- EF Core uses localdb with the `IisLogDb` connection string.
- Log ingestion stores:
	- `LogFile` (metadata + counts)
	- `LogRawLine` (one row per line, includes parse status)
	- `LogEntry` (parsed fields + normalized timestamp + original date/time text)
- Per-file limit: 10MB (zip entries are validated individually).

## Troubleshooting
- Ensure the backend is in a trusted zone for NTLM to flow to the browser.
- If you see 401s, verify Windows Authentication settings and browser policy.

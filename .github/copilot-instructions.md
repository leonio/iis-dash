# GitHub Copilot Instructions

This repo is a Vue 3.5 + Vite + Tailwind v4 frontend and a .NET 10 Minimal API backend with NTLM auth.

## General guidance
- Prefer the **latest stable TypeScript and C# language features** (TS 5.9+, C# 13+).
- Keep changes minimal and consistent with existing patterns.
- Use type-safe APIs; avoid `any`, broad `try/catch`, or silent fallbacks.

## Frontend (Vue + TypeScript)
- Use `<script setup lang="ts">` and strongly-typed props/emits.
- Prefer `ref`/`computed` and `defineProps`/`defineEmits` over Options API.
- Keep Tailwind v4 CSS-first configuration in `src/app.css`.
- Favor path aliases (`@/`) and update `tsconfig.app.json` when adding aliases.
- When adding UI, prefer shadcn-vue components under `src/components/ui`.
- For API calls, use `fetch` with `credentials: 'include'` to preserve NTLM tokens.

## Backend (.NET 10 + C#)
- Use **Vertical Slice Architecture (VSA)**: Group logic (Request, Response, Endpoint, Validation) into feature-based folders (e.g., `server/Features/Auth/GetUserInfo/`).
- Use **Endpoints** pattern: Each slice should define its own endpoint(s) using a static `Map` method or similar, avoiding a single massive `Program.cs`.
- Implement **Validation** using `.NET Minimal Endpoints Validation with source generation`: Prefer `FluentValidation` with source-generated validators or built-in `DataAnnotations` supported by the Request Delegate Generator (RDG).
- Keep authentication configured with `Microsoft.AspNetCore.Authentication.Negotiate`.
- CORS must allow `http://localhost:5173` and `AllowCredentials()`.
- Avoid controllers; favor `TypedResults` for type-safe API responses.

## Code style
- Keep formatting consistent with existing files.
- Favor small, focused changes; update tests if behavior changes.
- Do not add new tooling or frameworks unless explicitly requested.

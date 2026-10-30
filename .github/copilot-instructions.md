# GitHub Copilot Instructions

IIS Dash: a .NET 10 web app compiled with Native AOT to one executable per platform. Pages are rendered on the server
with RazorSlices and moved between with htmx 4; UIkit 3 for components; SQLite with plain SQL. See README.md.

## General guidance
- Prefer the latest stable C# language features.
- Keep changes minimal and consistent with existing patterns; match the comment style (say what and why, in full sentences).

## Structure
- **Vertical Slice Architecture**: a feature is one folder under `src/IisDash/Features/` with its routes (a static
  `Map` method), its SQL and its `.cshtml` slices. Cross-cutting pieces live in `src/IisDash/Shared/`.
- No controllers, no MVC, no Blazor: minimal API routes return `Html.Page(Slice.Create(...))`.
- Slices that take several values get an `Args` class in a `<Slice>.Args.cs` partial next to them.

## Native AOT
- Everything must stay AOT and trim safe: `PublishAot` runs the analyzers on every build and warnings are errors.
- No reflection-based serialization or ORMs; no libraries that need reflection. Read rows with explicit mapping
  functions (`Db.Query(sql, args, r => ...)`).
- Static files, texts and the schema are embedded resources.

## Data
- SQL must work on the SQLite that ships with older Windows (winsqlite3): no `RETURNING`, `NULLS LAST`, window functions
  or UPSERT.
- Compute figures in SQL (aggregates), not by loading rows into memory.
- Times are integer seconds since 1970 on the log's own clock.

## UI
- Every user-visible text goes through `Ui.T(key)` with the key in both `Locales/en.json` and `Locales/de.json`.
- Numbers and dates through `Ui.F` (the language's conventions), never `ToString()` with a culture.
- Links inside the app use `Ui.Go(href)` (htmx morph into `#app`); the filter lives in the URL (`LogFilter`).
- Colours are CSS variables in `Static/app.css`, defined for light and dark.
- No authentication: the app is meant to sit behind a reverse proxy if it needs one.

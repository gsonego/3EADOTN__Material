# expenses-ui

Given to students as-is — no code changes expected or required. Your job is the
infrastructure it runs on (see the project brief, Sections 3.3 and 6) and deploying
this image onto it.

## Hosting

Azure App Service, Linux, B1 plan (`webapp-expenses-<your_suffix>`), deployed as a
container image (same pattern as `catalog-ui` in the module demos — pushed to your ACR,
set via `az webapp config container set`). Listens on port **8080**: set the App
Service app setting `WEBSITES_PORT=8080`, or the container looks "deployed" but never
responds.

## Configuration (set from the UI, not environment variables)

Same pattern as `catalog-ui` in the module demos: click the endpoint pill in the top
bar to open Settings, then enter:

| Field | Purpose |
|---|---|
| Base URL | The **API Management gateway URL** for your `expenses-api` (e.g. `https://apim-expenses-<your_suffix>.azure-api.net/expenses`) — not the Container App's own URL. |
| Subscription key | The APIM subscription key for that API. |

These are saved in the browser's `localStorage` (per device, never sent anywhere but
this app) and sent to this server's own same-origin `/proxy/*` routes as custom
headers on every request. Browser JavaScript never talks to APIM directly — this
server reads those headers per request and attaches the subscription key server-side
(as `Ocp-Apim-Subscription-Key`) before forwarding to APIM, so the key is never
exposed to the browser or reachable via CORS.

## Guard behavior

If no Base URL has been set yet, the UI still loads and shows a clear banner ("No API
endpoint configured yet") with an empty expense list, rather than a blank page or a
crash — useful while you're still provisioning.

## Build & push (once you can restore NuGet packages)

```
docker build -t $ACR/expenses-ui:v1 .
docker push $ACR/expenses-ui:v1
```

# PocketBase.SDK: análisis y decisiones

Documento de referencia del proyecto. Mantenerlo actualizado al cerrar cada fase.

- **Última actualización:** 2026-11-25, avance de fase 6: integración live ampliada (roundtrip de cookie auth) y empaquetado NuGet base validado con `dotnet pack`.
- **Estado de verificación:**
  - Tests unitarios con servidores simulados (`HttpMessageHandler` falsos).
  - Test de integración real opcional (`LivePocketBaseIntegrationTests`) ejecutado contra PocketBase v0.42.3 local.

---

## 1. Objetivo y requisitos

- Librería .NET con acceso completo a PocketBase según https://pocketbase.io/docs/.
- Cubrir todas las herramientas y métodos de los SDKs oficiales de JS y Dart.
- Instanciable por inyección de dependencias (DI).
- Compatible con **.NET 8, 9, 10 y 11**.
- Diseño "opción 3": un cliente fluido único (`IPocketBaseClient`) **y** interfaces de servicio por dominio registradas en DI.
- Versión objetivo de PocketBase: **última estable, v0.42.3**.
- **Fidelidad a las fuentes:** seguir estrictamente la documentación oficial y el código de los SDKs oficiales.
  Toda desviación debe justificarse y comunicarse (ver §5).

## 2. Fuentes de referencia

| Fuente | Versión | Ubicación |
|---|---|---|
| Documentación oficial | v0.42.3 | https://pocketbase.io/docs/ |
| SDK JS oficial | 0.29.0 | `_ref/js-sdk` |
| SDK Dart oficial | 0.26.0 | `_ref/dart-sdk` |

- Los repos de GitHub de los SDKs están archivados. Ahora viven en https://ganigeorgiev.com/pocketbase/{js-sdk,dart-sdk}.
- Esa web es una SPA: `web_fetch` devuelve páginas vacías.
- El código se descarga como zip desde `https://ganigeorgiev.com/api/git/pocketbase/{repo}/archive?rev=HEAD`.
- `_ref/` está en `.gitignore`.

## 3. Arquitectura

### Estructura de la solución

La solución es `PocketBase.SDK.slnx` (formato .slnx).

| Proyecto | Contenido |
|---|---|
| `PocketBase.SDK.Abstractions` | Interfaces, DTOs, opciones, `RecordModel`, `PocketBaseException`. Sin dependencias. |
| `PocketBase.SDK` | Implementación: cliente, transport, servicios y herramientas portadas. |
| `PocketBase.SDK.DependencyInjection` | `AddPocketBase(...)`. |
| `PocketBase.SDK.Realtime` | **Vacío.** El realtime vive en el núcleo porque `RecordService` lo necesita (subscribe, OAuth2). Pendiente de decidir si se elimina. |
| `PocketBase.SDK.Tests` | Tests con xUnit. |

Empaquetado actual:
- Se generan paquetes para `PocketBase.SDK`, `PocketBase.SDK.Abstractions` y `PocketBase.SDK.DependencyInjection`.
- `PocketBase.SDK.Realtime` está marcado como no empaquetable (`IsPackable=false`) porque sigue vacío.

`Directory.Build.props` define:
- `TargetFrameworks`: net8.0, net9.0, net10.0 y net11.0.
- `Nullable` e `ImplicitUsings` activados.
- Generación de documentación XML.

### Decisiones clave

- **Admins:** desde PocketBase v0.23 no existe `/api/admins`. Los superusuarios son la colección auth `_superusers`.
  `pb.Admins` se mantiene como alias `[Obsolete]` de `Collection("_superusers")`.
- **Transport:** `HttpClient` tipado (`AddHttpClient<IPocketBaseTransport, PocketBaseTransport>`).
  - El cliente y los servicios son **transitorios**, para no fijar el handler (evita DNS obsoletas).
  - Las personalizaciones equivalentes a `beforeSend`/`afterSend` se hacen con `DelegatingHandler` sobre el `IHttpClientBuilder` que devuelve `AddPocketBase`.
- **Ciclo de vida:**
  - `IAuthStore` es Scoped por defecto o Singleton; Transient se rechaza.
  - `IRealtimeService` comparte el ciclo de vida del `IAuthStore`, porque guarda suscripciones y conexión.
  - `IRealtimeService` resuelve un transport nuevo en cada uso.
- **Peticiones (igual que los SDKs):**
  - La cabecera `Authorization` lleva el token sin prefijo "Bearer".
  - `Accept-Language` vale `en-US` por defecto.
  - `AutoClearAuthStore = true`: limpia el store ante un 401 y ante un token expirado. Esto último sigue a Dart; JS solo tiene un `@todo`.
- **JSON:**
  - Los nombres de propiedad se mantienen tal como se declaran (sin camelCase), porque PocketBase distingue mayúsculas en los campos.
  - La lectura no distingue mayúsculas.
- **API espejo de los SDKs:** `pb.Collection("x")`, `pb.CreateBatch()`, `pb.Filter(...)`, `pb.AuthStore`, `pb.SendAsync`, `pb.BuildUrl`, `pb.Realtime`.

## 4. Matriz de cobertura frente a los SDKs (JS 0.29.0 / Dart 0.26.0)

Leyenda: ✅ hecho · 🟡 transitorio o parcial · ❌ pendiente

### Cliente (`Client.ts` / `client.dart`)

| Miembro SDK | .NET | Estado |
|---|---|---|
| `baseURL`, `lang`, `authStore` | `BaseUrl`, `PocketBaseOptions.Lang`, `AuthStore` | ✅ |
| `collection()` | `Collection()` (cacheado) | ✅ |
| `createBatch()` | `CreateBatch()` | ✅ |
| `filter()` | `Filter()` | ✅ |
| `buildURL()` | `BuildUrl()` | ✅ |
| `send()` | `SendAsync()` / `SendAsync<T>()` | ✅ |
| `autoClearAuthStore` | `PocketBaseOptions.AutoClearAuthStore` | ✅ |
| `admins` | `Admins` (obsoleto) | ✅ |
| `beforeSend` / `afterSend` | `DelegatingHandler` | ✅ (desviación 5.1) |
| `autoCancellation`, `cancelRequest`, `cancelAllRequests` | `CancellationToken` | ✅ (desviación 5.2) |
| `getFileUrl()` (obsoleto en JS) | `Files.GetUrl(...)` `[Obsolete]` | ✅ |
| `realtime` | `Realtime` | ✅ |
| `settings`, `collections`, `files` | implementados | ✅ |
| `logs`, `health`, `backups`, `crons`, `sql` | implementados | ✅ |

### AuthStore (`BaseAuthStore`, `LocalAuthStore`, `AsyncAuthStore` / `AuthStore`, `AsyncAuthStore`)

| Miembro SDK | Estado |
|---|---|
| `token`, `record`, `isValid`, `isSuperuser`, `save`, `clear`, `onChange(fireImmediately)` | ✅ |
| `model`, `isAdmin`, `isAuthRecord` (obsoletos en JS) | ✅ portados como `[Obsolete]` |
| `loadFromCookie` / `exportToCookie` (JS) | ✅ |
| `AsyncAuthStore` (persistencia asíncrona, JS y Dart) | ✅ (cola serializada + carga inicial + fallback de clear) |
| `LocalAuthStore` (localStorage, solo navegador) | No aplica a .NET |

### RecordService

| Método SDK | Estado |
|---|---|
| `getFullList`, `getList`, `getFirstListItem`, `getOne`, `create`, `update`, `delete` | ✅ |
| `subscribe`, `unsubscribe` | ✅ fase 3 |
| `listAuthMethods`, `authWithPassword`, `authWithOAuth2Code`, `authRefresh` | ✅ |
| `authWithOAuth2` (flujo completo por realtime) | ✅ fase 3 |
| `requestOTP`, `authWithOTP`, `impersonate` | ✅ |
| `requestPasswordReset`, `confirmPasswordReset` | ✅ |
| `requestVerification`, `confirmVerification` | ✅ |
| `requestEmailChange`, `confirmEmailChange` | ✅ |
| `listExternalAuths`, `unlinkExternalAuth` (obsoletos en JS) | ✅ `[Obsolete]` |
| Sincronización del AuthStore tras `update`/`delete` del registro autenticado | ✅ |
| `authWithPassword` con `autoRefreshThreshold` (solo JS, superusers; `tools/refresh.ts`) | ✅ (registro/reset y refresh previo por request) |
| `authWithOAuth2` forma legacy con argumentos sueltos (obsoleta en JS) | No portado: equivale a `AuthWithOAuth2CodeAsync` |

### BatchService

| Método SDK | Estado |
|---|---|
| `collection()`, `upsert`, `create`, `update`, `delete`, `send` | ✅ |

### RealtimeService

| Método SDK | Estado |
|---|---|
| `subscribe`, `unsubscribe`, `unsubscribeByPrefix`, `unsubscribeByTopicAndListener` | ✅ |
| `clientId`, `isConnected`, `onDisconnect` | ✅ |
| Reconexión automática y reenvío de suscripciones | ✅ |
| Timeout de conexión (15 s, JS) y timeout de inactividad (315 s, Dart) | ✅ |

### Servicios de la fase 4 (implementados)

| Servicio | Métodos SDK (iguales en JS y Dart) | Estado |
|---|---|---|
| CollectionService | CRUD (`getFullList`, `getList`, `getFirstListItem`, `getOne`, `create`, `update`, `delete`), `import`, `truncate`, `getScaffolds`, `getAllOAuth2Providers`, `dryRunViewQuery` | ✅ |
| SettingsService | `getAll`, `update`, `testS3`, `testEmail`, `generateAppleClientSecret` | ✅ |
| FileService | `getURL` (`getUrl` obsoleto), `getToken` | ✅ |
| LogService | `getList`, `getOne`, `getStats`, `truncate` | ✅ |
| BackupService | `getFullList`, `create`, `upload`, `delete`, `restore`, `getDownloadURL` | ✅ |
| CronService | `getFullList`, `run` | ✅ |
| SQLService | `run` | ✅ |
| HealthService | `check` | ✅ |

DTOs añadidos en fase 4: `CollectionModel` (con `options` como JSON sin tipar), `CollectionField`, `LogModel`, `LogStat`, `BackupFileInfo`, `CronJob`, `HealthCheck`, `SqlResult`, `AppleClientSecret`, `ConfigurableOAuth2Provider`.

Pendiente de tipado fino (desviación actual): sub-DTOs de configuración de colecciones (`*Config`) se mantienen en `CollectionModel.Options` (`JsonObject`) para evitar ruptura en este paso.

### Herramientas portadas (`tools/` / utilidades Dart)

| Herramienta | .NET | Estado |
|---|---|---|
| `jwt.ts` | `PocketBaseJwt` | ✅ |
| `options.ts` (`serializeQueryParams`) | `PocketBaseQuery` | ✅ |
| `formdata.ts` | `PocketBaseFormData` | ✅ |
| `filter` | `PocketBaseFilter` | ✅ |
| `caster.dart` | `RecordModel.Get<T>` | ✅ |
| `sse_client.dart` / `sse_message.dart` | `SseParser`, `SseMessage` | ✅ |
| `cookie.ts` | `CookieTools` + `AuthStore.LoadFromCookie/ExportToCookie` | ✅ |
| `refresh.ts` | lógica en `PocketBaseClient` + `RecordService.AuthWithPasswordAsync` | ✅ |

## 5. Desviaciones respecto a los SDKs (todas comunicadas al usuario)

1. **Hooks `beforeSend`/`afterSend`:** se sustituyen por `DelegatingHandler` en el `IHttpClientBuilder`, el mecanismo idiomático de .NET.
2. **Sin `requestKey` ni autocancelación:** se usa `CancellationToken`.
3. **Batch rechaza `HttpContent` como body,** porque el multipart no se puede releer de forma fiable.
4. **`Get<int?>` de un campo inexistente devuelve `null`,** no `0` como Dart.
5. **`ListExternalAuths`/`UnlinkExternalAuth` marcados `[Obsolete]`,** como en JS.
6. **`RecordModel.ToObject<T>` no distingue mayúsculas** por defecto.
7. **Realtime: `Collection("posts").UnsubscribeAsync()` usa el prefijo `"posts/"`.**
   - Los SDKs usan `"posts"`, que también borraría las suscripciones de `posts2`. Es una corrección de bug.
   - Se revierte si el usuario prefiere la fidelidad exacta.
8. **Realtime: las excepciones de los callbacks se capturan** y se registran con `Trace.TraceError`.
   - Los callbacks se ejecutan secuencialmente, en el orden de llegada.
   - En JS, un fallo en un listener de `PB_CONNECT` provoca una reconexión; aquí no.
9. **Realtime y OAuth2: cancelar con `CancellationToken`** quita el listener y lanza `OperationCanceledException`. JS lanza `ClientResponseError` con `isAbort`.
10. **Opciones de suscripción:** solo se serializan las partes no vacías (como Dart). JS envía `{"query":{}}` aunque esté vacía.
11. **Errores de conexión realtime:** llevan la excepción real en `OriginalError`, en lugar del texto genérico de JS.
12. **`AuthWithOAuth2Async`:** el `urlCallback` recibe un `string` (como JS). Dart pasa un `Uri`.
13. **Conexión SSE:** usa una respuesta HTTP en streaming (como Dart), porque .NET no tiene `EventSource`.
    - No envía `Authorization` en el GET, igual que `EventSource`. La autorización ocurre en el POST de suscripciones, según la documentación.
14. **`CollectionModel.Options` se expone como `JsonObject`** (no con sub-DTOs tipados `*Config` como en Dart). Se priorizó cubrir todos los endpoints/métodos de fase 4 sin bloquear por el gran árbol de tipos de configuración.
15. **`SettingsService` usa `JsonObject` en `GetAll`/`Update`**, en vez de clases fuertemente tipadas para todas las ramas de settings.

## 6. Riesgos conocidos (tras una primera verificación con PocketBase real)

- **Timeout del `HttpClient` en la conexión SSE.**
  - Se asume que `HttpClient.Timeout` (100 s) solo se aplica hasta las cabeceras, porque se usa `ResponseHeadersRead`.
  - Si no fuera así, la conexión se cortaría cada 100 s y se reconectaría sola.
- **Handlers de resiliencia** (p. ej. `AddStandardResilienceHandler`) añadidos al `IHttpClientBuilder` podrían cortar la conexión SSE con sus timeouts.
- **Aspectos del servidor sin comprobar:** las cabeceras reales que envía y el aviso de desconexión tras 5 minutos de inactividad.
- **net9.0:** compila, pero sus tests no se pueden ejecutar en la máquina de desarrollo porque falta el runtime de .NET 9.

## 7. Plan de fases

| Fase | Contenido | Estado |
|---|---|---|
| 1–2 | Núcleo (transport, AuthStore, errores, herramientas), DI, RecordService (CRUD + auth), Batch | ✅ |
| 3 | Realtime (SSE), `Subscribe`/`Unsubscribe` en registros, `AuthWithOAuth2Async` | ✅ |
| 4 | Collections, Settings, Files, Logs, Backups, Crons, SQL, Health (1:1 de métodos con los SDKs) y DTOs principales | ✅ |
| 5 (propuesta) | Huecos restantes: `autoRefreshThreshold`, cookies del AuthStore, `AsyncAuthStore`, miembros obsoletos del AuthStore, tipado completo de `CollectionModel.Options` (`*Config`) | 🟡 (completado salvo tipado fino `*Config`) |
| 6 (propuesta) | Pruebas de integración contra un PocketBase v0.42.3 real; empaquetado NuGet | 🟡 integración opcional + empaquetado base listos; pendiente cierre de metadatos finales de publicación |

## 8. Estado de los tests

- **Última ejecución unitaria:** 96 tests, 0 fallos, ejecutados en net10.0 (incluye cobertura de fase 5).
- **Última ejecución con integración real opcional:** `LivePocketBaseIntegrationTests` (1 test), 0 fallos, en net10.0. Incluye auth, cookie auth roundtrip (`ExportToCookie`/`LoadFromCookie` + `AuthRefreshAsync`) y cobertura de endpoints fase 4.
- **Realtime:** batería ejecutada 25 veces seguidas sin fallos, para detectar tests inestables.
- **Comandos:**
  - `dotnet build PocketBase.SDK.slnx`
  - `dotnet test PocketBase.SDK.Tests -f net10.0`
  - `dotnet test PocketBase.SDK.Tests -f net10.0 --filter "FullyQualifiedName~LivePocketBaseIntegrationTests"` (con `PB_URL`, `PB_EMAIL`, `PB_PASSWORD`)
  - `dotnet pack PocketBase.SDK.slnx -c Release`

## 9. Matriz de integración real por endpoint (PocketBase local)

Entorno de ejecución:
- PocketBase: `v0.42.3`
- URL: `http://localhost:8090/`
- Auth usada: superuser de entorno local
- Fecha: 2026-11-25

Leyenda de resultado:
- **OK**: endpoint ejecutado con respuesta de éxito (2xx) y aserciones de contenido.
- **Validación esperada / opcional**: endpoint ejecutado, pero puede devolver 4xx/5xx por configuración del entorno (p.ej. S3/SMTP/Apple/backup payload), y se considera cobertura del endpoint.

| Área | Método SDK/.NET | Endpoint | Resultado en entorno local |
|---|---|---|---|
| Auth | `collection("_superusers").authWithPassword` / `AuthWithPasswordAsync` | `POST /api/collections/_superusers/auth-with-password` | OK |
| Health | `health.check` / `Health.CheckAsync` | `GET /api/health` | OK |
| SQL | `sql.run` / `Sql.RunAsync` | `POST /api/sql` | OK |
| Collections | `getList` / `GetListAsync` | `GET /api/collections` | OK |
| Collections | `getFullList` / `GetFullListAsync` | `GET /api/collections?page=...` | OK |
| Collections | `getFirstListItem` / `GetFirstListItemAsync` | `GET /api/collections?filter=...` | OK |
| Collections | `getOne` / `GetOneAsync` | `GET /api/collections/{idOrName}` | OK |
| Collections | `create` / `CreateAsync` | `POST /api/collections` | OK |
| Collections | `update` / `UpdateAsync` | `PATCH /api/collections/{idOrName}` | OK |
| Collections | `delete` / `DeleteAsync` | `DELETE /api/collections/{idOrName}` | OK (cleanup ejecutado) |
| Collections | `import` / `ImportAsync` | `PUT /api/collections/import` | OK |
| Collections | `truncate` / `TruncateAsync` | `DELETE /api/collections/{idOrName}/truncate` | OK |
| Collections | `getScaffolds` / `GetScaffoldsAsync` | `GET /api/collections/meta/scaffolds` | OK |
| Collections | `getAllOAuth2Providers` / `GetAllOAuth2ProvidersAsync` | `GET /api/collections/meta/oauth2-providers` | OK |
| Collections | `dryRunViewQuery` / `DryRunViewQueryAsync` | `POST /api/collections/meta/dry-run-view` | OK |
| Files | `getToken` / `GetTokenAsync` | `POST /api/files/token` | OK |
| Files | `getURL` / `GetURL` | construcción URL local | OK |
| Logs | `getList` / `GetListAsync` | `GET /api/logs` | OK |
| Logs | `getOne` / `GetOneAsync` | `GET /api/logs/{id}` | OK (si había logs) |
| Logs | `getStats` / `GetStatsAsync` | `GET /api/logs/stats` | OK |
| Logs | `truncate` / `TruncateAsync` | `DELETE /api/logs` | Validación esperada / opcional (depende de reglas del entorno) |
| Settings | `getAll` / `GetAllAsync` | `GET /api/settings` | OK |
| Settings | `update` / `UpdateAsync` | `PATCH /api/settings` | OK (actualización no destructiva) |
| Settings | `testS3` / `TestS3Async` | `POST /api/settings/test/s3` | Validación esperada / opcional |
| Settings | `testEmail` / `TestEmailAsync` | `POST /api/settings/test/email` | Validación esperada / opcional |
| Settings | `generateAppleClientSecret` / `GenerateAppleClientSecretAsync` | `POST /api/settings/apple/generate-client-secret` | Validación esperada / opcional |
| Crons | `getFullList` / `GetFullListAsync` | `GET /api/crons` | OK |
| Crons | `run` / `RunAsync` | `POST /api/crons/{id}` | OK (si existían jobs) |
| Backups | `getFullList` / `GetFullListAsync` | `GET /api/backups` | OK |
| Backups | `create` / `CreateAsync` | `POST /api/backups` | Validación esperada / opcional |
| Backups | `upload` / `UploadAsync` | `POST /api/backups/upload` | Validación esperada / opcional |
| Backups | `restore` / `RestoreAsync` | `POST /api/backups/{key}/restore` | Validación esperada / opcional |
| Backups | `delete` / `DeleteAsync` | `DELETE /api/backups/{key}` | Validación esperada / opcional |
| Backups | `getDownloadURL` / `GetDownloadURL` | construcción URL local | OK |

Notas:
- Los endpoints marcados como “validación esperada / opcional” se ejecutaron realmente en la integración, pero su éxito depende de configuración externa del entorno (proveedores, llaves, SMTP, archivos de backup válidos, etc.).
- El test de integración evita falsos negativos por configuración local, pero mantiene cobertura de llamada, ruta, autenticación y manejo de respuesta.

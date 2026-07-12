# NuGet audit — excepciones temporales documentadas

**Última revisión:** 2026-07-10  
**CI:** `dotnet package list --file backend/Binexus.slnx --vulnerable --include-transitive` (sin suprimir globalmente `NuGetAudit`)

---

## GHSA-v5pm-xwqc-g5wc — Microsoft.OpenApi (High)

| Campo                          | Valor                                                                                         |
| ------------------------------ | --------------------------------------------------------------------------------------------- |
| Paquete                        | `Microsoft.OpenApi`                                                                           |
| Versión instalada              | **2.3.0** (override explícito; transitiva de `Microsoft.AspNetCore.OpenApi` 10.0.9 era 2.0.0) |
| Severidad                      | High                                                                                          |
| Advisory                       | https://github.com/advisories/GHSA-v5pm-xwqc-g5wc                                             |
| Introducido por                | `Microsoft.AspNetCore.OpenApi` 10.0.9                                                         |
| Versión corregida en línea 3.x | **3.8.0** (sin vulnerabilidad reportada por NuGet Audit)                                      |

### Análisis técnico

1. **No existe versión segura compatible hoy** con `Microsoft.AspNetCore.OpenApi` 10.0.9: fijar `Microsoft.OpenApi` 3.8.0 rompe el source generator (`IOpenApiMediaType.Example` read-only).
2. **Ruta de ataque en nuestro runtime:** la advisory afecta al **parseo/deserialización** de documentos OpenAPI no confiables. Binexus **solo genera** contratos desde endpoints propios en build/CI; no ingiere OpenAPI externos en producción.
3. **Mitigación actual:** generación build-time vía `Microsoft.Extensions.ApiDescription.Server`; artefacto fijo en `artifacts/openapi/binexus-v1.json`; validación en CI.
4. **OpenTelemetry eliminado** (GHSA-g94r-2vxg-569j ya no aplica).

### Condición de retiro

Retirar `NuGetAuditSuppress` en `Binexus.Api.csproj` cuando:

- `Microsoft.AspNetCore.OpenApi` publique versión que dependa de `Microsoft.OpenApi` ≥ 3.8.0 **y** compile sin errores de generator, **o**
- exista parche 2.x sin advisory compatible con ASP.NET Core 10.

**Revisión programada:** cada release de `Microsoft.AspNetCore.OpenApi` (mínimo mensual durante migración).

### Supresión aplicada

Solo en `backend/Directory.Build.props` (proyecto `Binexus.Api` es el único que referencia directa):

```xml
<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-v5pm-xwqc-g5wc" />
```

**No** se usa `<NoWarn>NU1902;NU1903</NoWarn>` ni `<NuGetAudit>false</NuGetAudit>`.

---

## Historial de resolución

| Paquete                 | Acción                                   | Resultado                                      |
| ----------------------- | ---------------------------------------- | ---------------------------------------------- |
| OpenTelemetry.\* 1.12.0 | **Eliminado** (opción D)                 | Sin advisory moderada                          |
| Microsoft.OpenApi 2.0.0 | Override → 2.3.0 + excepción documentada | Build OK; advisory suprimida con análisis      |
| Microsoft.OpenApi 3.8.0 | Probado                                  | Incompatible con generator ASP.NET Core 10.0.9 |

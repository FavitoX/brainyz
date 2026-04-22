# brainyz — Decisiones de diseño

> Knowledge layer personal global, integrado con Claude Code vía MCP. Proyecto open source.
> Registro vivo de decisiones tomadas durante el diseño.
> Nombre del proyecto: `brainyz` · Comando CLI: `brainz` (alias: `bz`)

---

## D-001 — Propósito y alcance

**Estado**: Aceptada
**Contexto**: Favio quiere un "segundo cerebro" técnico para registrar decisiones, criterios y conocimiento acumulado. Herramientas existentes (Engram, Mem0) son por-proyecto o por-sesión, no sirven como capa global.
**Decisión**: Construir un sistema **global-first**, con scoping opcional por proyecto. El brain es el centro; los proyectos son vistas filtradas de él. **Proyecto open source** (publicado en GitHub), pensado para ser útil a otros devs/equipos con el mismo problema.
**Consecuencias**: Necesita vivir fuera de cualquier repo, en ubicación estándar del SO. Necesita mecanismo para resolver scope automáticamente (por cwd u otro marcador). Al ser OSS, todas las decisiones técnicas se revisan con lente de adopción, contribución y extensibilidad, no solo comodidad del autor.

---

## D-002 — Ubicación global del storage

**Estado**: Aceptada
**Contexto**: El brain debe ser accesible desde cualquier proyecto, persistente, y portable entre Mac/Linux.
**Decisión**: Archivo único en ruta XDG-compliant: `~/.config/brainyz/brainyz.db` en Linux, `~/Library/Application Support/brainyz/brainyz.db` en Mac, `%APPDATA%\brainyz\brainyz.db` en Windows.
**Consecuencias**: Backup = copiar un archivo. Portable. Fuera del control de cualquier repo git de proyecto. Potencialmente versionable en su propio repo privado si se desea.

---

## D-003 — Motor de base de datos

**Estado**: Aceptada
**Contexto**: Se evaluaron SQLite, LibSQL, pglite, DuckDB, LocalDB (SQL Server), Datomic, Turso Database (rewrite en Rust 2025).
**Hallazgo clave**: LibSQL es un *superset* de SQLite — 100% compatible hacia atrás, mismo formato de archivo, misma API. No se pierde funcionalidad, se ganan features.
**Drivers de la decisión**: Favio confirmó dos necesidades que SQLite puro no cubre bien:
1. **Embeddings** para búsqueda semántica → LibSQL tiene vector datatype nativo (sin `sqlite-vec` ni extensiones).
2. **Sync multi-device** en el horizonte cercano → LibSQL tiene embedded replicas que sincronizan con un server remoto (Turso Cloud o `libsql-server` self-hosted). Con SQLite puro habría que armar el sync manualmente o con herramientas de terceros (Litestream, cr-sqlite) que agregan complejidad.
**Decisión**: **LibSQL embebido** (modo archivo local, con puerta abierta a embedded replica cuando se active el sync).
**Alternativas consideradas**:
- *SQLite puro*: descartada porque resolver embeddings + sync requeriría stitch de múltiples extensiones/tools externos, contradiciendo el principio de simplicidad operativa.
- *Turso Database (rewrite Rust)*: interesante pero muy nuevo (2025). Los propios creadores lo recomiendan para proyectos nuevos pero reservan LibSQL para workloads mission-critical hoy. Descartada por madurez.
- *LocalDB*: descartada por ser solo Windows.
- *DuckDB*: descartada por estar orientada a analytics/OLAP; el caso de uso es transaccional/lookup.
- *pglite*: descartada por madurez y footprint mayor. Se reconsideraría si aparece necesidad de jsonb serio o FTS multi-idioma avanzado.
- *Datomic/XTDB*: descartadas por complejidad desproporcionada.
**Consecuencias**: Tooling compatible con SQLite (DB Browser, Datasette, DBeaver abren el archivo). FTS5 built-in. Vector search nativo para embeddings. Footprint similar a SQLite. Schema se mantiene 100% SQLite-compatible, así que "volver" a SQLite puro es trivial si aparece algún problema. Cliente oficial en el lenguaje que se elija para el MCP (D-008).
**Alternativas consideradas**:
- *SQLite puro*: la opción segura, máxima madurez y tooling. Sigue siendo válida; la única razón para moverse a LibSQL es vector datatype nativo y puerta abierta a sync.
- *Turso Database (rewrite Rust)*: interesante pero muy nuevo (2025). Los propios creadores lo recomiendan para proyectos nuevos pero reservan LibSQL para workloads mission-critical hoy. Descartada por madurez.
- *LocalDB*: descartada por ser solo Windows.
- *DuckDB*: descartada por estar orientada a analytics/OLAP; el caso de uso es transaccional/lookup.
- *pglite*: descartada por madurez y footprint mayor. Se reconsideraría si aparece necesidad de jsonb serio o FTS multi-idioma avanzado.
- *Datomic/XTDB*: descartadas por complejidad desproporcionada.
**Consecuencias**: Tooling compatible con SQLite (DB Browser, Datasette, DBeaver abren el archivo). FTS5 built-in. Vector search nativo si se decide meter embeddings (D-009). Footprint similar a SQLite. Cliente oficial en el lenguaje que se elija para el MCP (D-008). Puerta abierta a Turso Cloud para sync multi-device sin migración.
**Riesgo asumido**: tooling de terceros menos maduro que el de SQLite; en la práctica, irrelevante mientras se mantenga compatibilidad.

---

## D-004 — Contenido en base, con formato Markdown

**Estado**: Aceptada
**Contexto**: Se evaluó modelo híbrido (SQLite + archivos `.md` en disco, estilo ADR) vs todo-en-base.
**Decisión**: Todo el contenido vive dentro de la base como TEXT, **pero con formato Markdown adentro**. El storage es una sola fuente (SQLite), pero el formato del texto permite énfasis, listas, code blocks, links, headers, etc.
**Rationale**: Un solo lugar para consultar y sincronizar, queries relacionales directas, audit log en la misma base, pero sin perder expresividad del texto. Markdown es plaintext: sigue siendo greppable, diffable por humanos, y FTS5 lo indexa sin problema (los caracteres `*`, `#`, `` ` `` no afectan la búsqueda).
**Consecuencias**:
- Front y CLI deben renderizar Markdown al mostrar (cualquier librería estándar lo hace).
- Edición: el editor del front acepta Markdown; `brainz edit <id>` abre `$EDITOR` con el contenido y lo guarda de vuelta.
- Los embeddings se generan sobre el texto tal cual (con markdown); los modelos modernos lo manejan bien, y de hecho los headers/énfasis dan señal semántica útil.
- Para FTS5, opcional: se puede indexar una versión "stripped" del markdown si se quiere búsqueda más limpia, pero probablemente no haga falta.

---

## D-005 — Modelo global-first con scoping

**Estado**: Aceptada
**Contexto**: Necesidad de separar conocimiento transversal del específico por proyecto.
**Decisión**: Tabla única de decisiones con `project_id` nullable. NULL = global. Resolución automática del scope por `cwd` al invocar desde Claude Code, con override explícito.
**Consecuencias**: Permite queries cross-project, detección de contradicciones, y "promoción" de decisiones de proyecto a global cuando se identifica un patrón recurrente.

---

## D-006 — Separación entre Principios y Decisiones

**Estado**: Aceptada
**Contexto**: No todo el conocimiento tiene la misma naturaleza. Hay convicciones estables ("auth obligatoria en todo handler", "nunca concatenar SQL") y hay decisiones contextuales ("en AILang usamos S-expressions").
**Decisión**: Dos entidades separadas:
- **Principles**: convicciones transversales, estables, se exponen siempre al inicio de sesión de Claude Code.
- **Decisions**: registro histórico contextual, se consultan on-demand por búsqueda.
**Consecuencias**: El MCP tendrá un `get_principles()` que Claude llama al inicio o bajo demanda. Las decisiones requieren query explícita. Un principio puede nacer como una decisión que se "promueve".

---

## D-007 — Integración con Claude Code vía MCP

**Estado**: Aceptada
**Contexto**: Necesidad de que Claude Code acceda al brain desde cualquier repo sin configuración por-proyecto.
**Decisión**: Implementar un MCP server local, registrado globalmente en Claude Code. Tools mínimas: `search`, `get`, `add`, `link`, `find_similar`, `find_contradictions`, `get_principles`, `promote_to_global`, `list_projects`.
**Consecuencias**: Una sola configuración de MCP, disponible en todos los proyectos. El server corre local, sin red.

---

## D-009 — Búsqueda semántica con embeddings

**Estado**: Aceptada
**Contexto**: La búsqueda puramente textual (FTS5) falla cuando se busca un concepto expresado con palabras diferentes. Ejemplo: buscar "reintentos" debería encontrar decisiones sobre Polly, circuit breakers, resilience, retry policies, aunque esas palabras exactas no aparezcan.
**Decisión**: Búsqueda híbrida. FTS5 para matches literales + vector search sobre embeddings para similitud semántica. Se combinan resultados con un ranking simple (reciprocal rank fusion o similar).
**Consecuencias**:
- Vector datatype nativo de LibSQL (confirmado en D-003) resuelve storage e índice sin dependencias externas.
- Cada decisión/principio tiene un embedding asociado que se regenera al editar el contenido relevante (title + context + decision + rationale).
- Necesidad de un provider de embeddings. Pendiente: **D-017**.
- Costo: si se usa API externa, ~centavos por mes para uso personal. Si se usa modelo local, cero costo operativo.

---

## D-010 — Resolución de scope por proyecto

**Estado**: Aceptada
**Contexto**: Cuando Claude Code (o el CLI) consulta el brain desde un directorio, necesita saber en qué "proyecto" está para filtrar decisiones. Se evaluaron varios mecanismos.
**Decisión**: **Cadena de precedencia**, se resuelve de arriba hacia abajo y gana el primer match:
1. **Archivo `.brain` en el repo** (o ancestros del cwd): override explícito. Contiene `project_id` o nombre. Máxima prioridad porque el usuario lo puso a propósito.
2. **Detección por git remote**: si el repo tiene un `origin` conocido, se matchea contra la tabla `projects` (campo `git_remote`). Automático y confiable para repos ya mapeados.
3. **Mapeo por path en config global**: `~/.config/brainyz/config.toml` (Linux/Mac) o `%APPDATA%\brainyz\config.toml` (Windows) con `[projects.paths]` mapeando rutas → `project_id`. Útil para directorios que no son git.
4. **Fallback**: scope global (sin `project_id`). Solo ve decisiones globales; no filtra por proyecto.
**Consecuencias**:
- El primer uso de brainyz en un repo nuevo funciona en modo global; el usuario puede agregar `.brain` o registrar el proyecto después.
- El MCP expone `resolve_scope(cwd)` para que se pueda debuggear qué scope se está aplicando.
- `brainz init` en un repo crea el `.brain` y registra el proyecto (+ git remote si existe) de una pasada.

---

## D-012 — Versionado / historia de cambios

**Estado**: Aceptada
**Contexto**: Se aclaró una duda conceptual importante: el sync de LibSQL (embedded replicas) replica el *estado actual* de la base entre nodos — **no es versionado**, es replicación. Para tener historia de cambios ("¿qué decía esta decisión el mes pasado?", "¿cuándo cambió este principio?") hay que armarla explícitamente.
**Alternativas consideradas**:
- *Triggers `AFTER UPDATE/DELETE`* que copian la fila vieja a tabla `_history`: nativo SQL, automático, cero código aplicación.
- *Event sourcing (append-only con `version_number` + `is_current`)*: más puro, pero complica queries habituales.
- *Snapshots JSON*: flexible pero pesado y menos queriable.
- *Git sobre el `.db`*: no sirve, formato binario.
**Decisión**: **Triggers `AFTER UPDATE` y `AFTER DELETE`** sobre `decisions` y `principles`, que copian la fila afectada a tablas espejo `decisions_history` / `principles_history` con `changed_at` y `change_type` ('update' | 'delete'). Simple, automático, invisible al código de aplicación, y con queries triviales para reconstruir historia.
**Consecuencias**:
- Crecimiento de la base con cada edición; aceptable para uso personal (estimado: MB por año).
- Queries de historia: `SELECT * FROM decisions_history WHERE decision_id = ? ORDER BY changed_at DESC`.
- Se puede exponer por el MCP como `get_history(id)` o `diff(id, date)`.
- El sync de LibSQL replica también las tablas `_history`, así que la historia queda consistente entre máquinas.

## D-018 — Estrategia de sync multi-device

**Estado**: Aceptada
**Contexto**: Al ser OSS, depender de un servicio pago (Turso Cloud) como default contradice la filosofía del proyecto y baja adopción. Los usuarios quieren poder auto-hostearse su propio sync.
**Decisión**: **`libsql-server` self-hosted** como recomendación primaria. Apache 2.0, binario único, corre en cualquier VPS chico, Docker-friendly. El proyecto va a documentar cómo levantar uno (incluyendo `docker-compose.yml` de referencia). Turso Cloud queda como opción válida pero no default, para usuarios que prefieran managed.
**Consecuencias**:
- Docs del proyecto incluyen guía de deploy de `libsql-server` (VPS + Docker).
- El cliente de brainyz configura la URL del server en `~/.config/brainyz/config.toml` (o equivalente por plataforma, ver D-002).
- Para uso single-device, sync simplemente no se activa — el archivo local alcanza.
- Autenticación entre cliente y server: JWT token en config. La generación del token se documenta.

---

## D-008 — Lenguaje/runtime del MCP server y CLI

**Estado**: Aceptada
**Contexto**: Se evaluaron Rust, Go, C#, TypeScript/Node, Python bajo dos lentes: (1) adopción potencial en el ecosistema OSS devtools, (2) motivación sostenida del autor para mantenerlo durante años.
**Decisión**: **C# con .NET 10 LTS + NativeAOT**. Un único binario nativo por plataforma, sin runtime externo, distribuido vía GitHub Releases + posiblemente `brew` / `winget`.
**Rationale**:
- **Motivación sostenida > adopción teórica.** Un proyecto OSS vive o muere por la energía sostenida del mantainer. Favio es más productivo, disfruta más, y escribe mejor código en C#.
- **NativeAOT en .NET 10 está maduro.** Binarios de ~10-15MB, cero dependencias de runtime, arranque instantáneo, cross-platform (Linux, macOS, Windows).
- **Performance irrelevante como diferenciador.** El bottleneck real del brain es I/O (LibSQL, API de embeddings), no CPU. Go no habría ganado nada medible.
- **Consistencia con stack personal.** AILang transpiler también está en C#; mantener un solo stack personal reduce fricción mental.
- **Ecosistema sólido:** `Microsoft.Data.Sqlite` funciona contra archivos LibSQL (compatibilidad binaria), Polly disponible para resilience de calls a providers de embeddings, `System.CommandLine` para el CLI, SDK MCP oficial para C# existe y está activo.
**Alternativas consideradas y descartadas**:
- *Go*: la elección "óptima de adopción OSS" pero contra el gusto personal. Riesgo de desmotivación en proyecto de largo plazo. Se prefirió el gusto + AOT.
- *Rust*: curva de aprendizaje demasiado alta para el tiempo disponible; habría atrasado el proyecto meses.
- *TypeScript/Node*: máxima audiencia potencial de contribuidores pero runtime pesado y performance inferior para un CLI embebido.
- *Python*: excelente para embeddings pero `pip install` + venvs es fricción alta para devtools en 2026.
- *Dual implementation (C# + Go)*: explícitamente rechazada. Doble trabajo, divergencia inevitable, dilución de atención, ninguna implementación queda bien.
**Consecuencias**:
- Stack: .NET 10 LTS, NativeAOT, `Microsoft.Data.Sqlite` (compat LibSQL), Polly, `System.CommandLine`, SDK MCP oficial.
- Build: CI con matrix de `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, publica binarios en GitHub Releases.
- Adopción: menor alcance inicial que Go en el ecosistema OSS devtools; se compensa con calidad del producto y docs claras.
- Tests: xUnit. Reflection limitada por AOT → hay que elegir libs con soporte AOT (ej: System.Text.Json con source generators, evitar libs que dependan fuerte de reflection).
- Verificación pendiente al momento de implementar: confirmar que `Microsoft.Data.Sqlite` abre archivos LibSQL con vector datatype sin issues, o usar `Libsql.Client` de NuGet. No bloqueante — ambos clientes existen y están soportados.
- Constraint común a todos los lenguajes nativos: sin plugins dinámicos en runtime. Ver D-023 para estrategia de extensibilidad.

---

## D-022 — Modelo de comunidad

**Estado**: Aceptada
**Contexto**: Al ser OSS, hay que decidir cómo se gobierna el proyecto desde el arranque. Se evaluaron tres modelos: (A) proyecto personal publicado, (B) benevolent dictator, (C) community-driven desde día uno.
**Decisión**: **Modelo A al arrancar, con camino claro a B si gana tracción.** El proyecto se publica como "mío, abierto", no como "de la comunidad". La visión técnica la define Favio; PRs chicos (bugs, typos, mejoras claras) son bienvenidos; features grandes requieren issue previo de discusión; la dirección es del autor.
**Rationale**:
- La visión técnica es fuerte y opinionada; un modelo community-driven diluiría coherencia.
- Los primeros 6-12 meses requieren velocidad de iteración, incompatible con governance pesado.
- Evolucionar A → B cuando gana tracción es natural; C → B cuando el proceso es overhead es traumático.
- El producto aún no está validado en adopción; meter governance antes de saber si sirve es trabajo perdido.
**Consecuencias prácticas**:
- `README.md` claro y sin hype, con demo corto.
- `CONTRIBUTING.md` minimalista ("abrí un issue antes de un PR grande, tests requeridos, formato estándar").
- Issues abiertos, sin SLA prometido.
- Roadmap público pero propio (`ROADMAP.md` o GitHub Projects).
- Sin Code of Conduct elaborado al arranque (one-liner alcanza; se adopta Contributor Covenant si escala).
- Sin Discord/Slack al arranque; GitHub Discussions opcional.
- CI básico: tests + lint + build multi-plataforma.
- Changelog en formato keep-a-changelog.
**Señales para migrar a Modelo B**: >5 contribuidores recurrentes, issues/PRs superando capacidad de review semanal, oferta de co-mantainer con fit técnico.
**Nota**: Este modelo no cierra puertas a monetización futura (servicio managed, features enterprise) con licencia permissive + capa comercial encima.

---

## D-019 — Nombre del proyecto y del comando CLI

**Estado**: Aceptada
**Contexto**: Se exploraron múltiples nombres con criterios: disponibilidad en GitHub/npm/dominios, no colisión con herramientas existentes, pronunciable, memorable, con personalidad. Se descartaron opciones con "brain" literal por saturación del espacio semántico (`glthr/brAIn`, `bloomedai/brain-cli`, `pelkmanslab/brainy`, `box/brainy`, `duanguang/brain-cli`). Se evaluaron también alternativas no-brain: `synapse`, `cortex`, `recall`, `ledger`, `atlas`.
**Decisión**:
- **Nombre del proyecto**: `brainyz` (repo, dominio, identidad).
- **Comando CLI**: `brainz` (binario, uso diario).
- **Alias recomendado en docs**: `bz` para quien quiera algo ultra-corto.
**Rationale**:
- `brainyz` está libre en GitHub y probablemente en registries principales.
- Mantiene la familia "brain" pero evita homónimos directos.
- El "z" final le da personalidad informal, memorable, alineado con el tono OSS-con-carácter.
- Patrón `nombre-largo/comando-corto` está establecido (kubernetes/kubectl, github cli/gh, ripgrep/rg).
- `brainz` como comando es divertido, corto, y suena bien en terminal.
**Alternativas consideradas para el comando**:
- *`brainctl`* (patrón `-ctl` estilo `kubectl`/`systemctl`): libre, transmite seriedad técnica y cero colisiones. Descartada porque el tono "corporate/infra" choca con la personalidad del proyecto. La comunidad OSS devtools moderna (ripgrep, bat, fd, zoxide, starship) se inclina a nombres con personalidad sobre patrones system-tool.
- *`bz`* como comando principal: muy corto pero requiere memorización, pierde la conexión visual con el nombre del proyecto. Queda como alias opcional, no como comando principal.
**Colisiones conocidas con el binario `brainz`** (aceptadas por pertenecer a nichos completamente distintos):
- *Varnish Controller Brainz*: binario server-side de producto comercial Varnish, típicamente instalado vía systemd en servidores de CDN/caching. Audiencia y contexto de instalación diferentes.
- *Brainease* (esolang tipo brainfuck): binario `brainz` instalado vía `cargo install brainease`. Nicho esotérico muy chico.
- *BrainZ* (esolang): sin binario instalable mainstream.
**Mitigación**: el alias `bz` queda documentado desde el día 1 como alternativa oficial para cualquier usuario que tenga conflictos de PATH.
**Ejemplo de uso**:
```bash
brainz init
brainz add "Elegimos LibSQL para el motor"
brainz search "resilience patterns"
brainz sync
brainz principle "Auth mandatory en todo handler"
```
**Pendiente de verificar al momento de crear el repo**: disponibilidad de dominios (`brainyz.dev`, `brainyz.sh`, `brainyz.io`), handle de GitHub org si se crea, namespace en Homebrew y winget.

---

## D-024 — Tercera entidad: Notes (información de referencia)

**Estado**: Aceptada
**Contexto**: Al diseñar el schema surgió que hay un tipo de contenido que no encaja ni en Decisions ni en Principles: información descriptiva de referencia. Ejemplos: "Stripe v2024-01 cambió el comportamiento de refunds", "El equipo de pagos usa Linear, no Jira", "La regex para validar CUIT es X", "Anthropic recomienda Sonnet 4.6 para tool use". Esto no es una decisión (no hay alternativas ni rationale), y no es un principio (no es prescriptivo, es descriptivo).
**Decisión**: Agregar una tercera entidad **`notes`** con semántica clara: información descriptiva de referencia, no prescriptiva, sin obligación de rationale ni alternativas. Campos mínimos: `title`, `content` (Markdown), `source` opcional (URL/origen), `active` flag para archivar info obsoleta.
**Modelo resultante** — tres entidades con semántica discriminada:
- **Decision**: elección razonada entre alternativas (context + decision + rationale obligatorios).
- **Principle**: convicción prescriptiva estable (statement obligatorio).
- **Note**: información descriptiva de referencia (content obligatorio, sin prescribir acción).
**Consecuencias**:
- `notes` hereda la misma infra: scoping por project, tags, FTS5, embeddings, history.
- En el MCP: tools `note_add`, `note_search`, `get_notes`, etc. Las notes se incluyen en búsquedas semánticas por default.
- Una note puede *promoverse* a principle/decision si cambia de naturaleza (ej: "Stripe cambió refunds" → "Usamos Stripe v2024-01 como versión congelada").
- Alternativas descartadas: (a) meter todo en principles con distorsión conceptual, (b) status `informational` en decisions con muchos NULL, (c) tabla genérica `entries` con type discriminado (pérdida de constraints específicos).

---

## D-025 — Retención y compactación de history

**Estado**: Pendiente de resolver antes de v1.0 (no bloqueante para v0.1)
**Contexto**: Las tablas `_history` crecen con cada edit. Para uso personal el crecimiento es lento (MB por año), pero a largo plazo hay que gestionarlo.
**Opciones a evaluar**:
- Retention policy configurable: `brainz vacuum --history-older-than 6months`.
- Snapshot consolidation: cada N updates consolidar en snapshot mensual.
- Archivar a archivo externo separado (`brainyz-history-YYYY.db`).
- Storage de diffs en vez de snapshots completos (complejo pero compacto).
**Estado actual**: dejamos crecer libremente en v0.1. Cuando se acerque a 100MB de history, implementar vacuum con retention configurable (opción 1) como primera medida, escalando a snapshot consolidation si no alcanza.

---

## D-011 — Schema v0.1

**Estado**: Aceptada (v0.1 implementada en `schema.sql`)
**Contexto**: Definir el DDL concreto para LibSQL con todas las entidades decididas.
**Decisión**: Schema de 10 tablas + 3 virtual tables (FTS5) + 18 triggers, escrito en SQL compatible LibSQL/SQLite.
**Entidades core**:
- `projects` — scopes opcionales con `git_remote` para detección automática.
- `decisions` — decisiones razonadas (context + decision + rationale + consequences + alternatives).
- `principles` — convicciones estables (statement + rationale); flag `promoted_from` si nació de una decision.
- `notes` — información de referencia (content + source); ver D-024.
- `alternatives` — opciones descartadas con `why_rejected` obligatorio.
- `tags` jerárquicos vía path, con M:N a decisions/principles/notes.
- `decision_links` para grafo de relaciones (supersedes, relates_to, depends_on, conflicts_with, informed_by, split_from).
**Infra de soporte**:
- **FTS5** en `decisions_fts`, `principles_fts`, `notes_fts` con tokenizer unicode61 (anda bien con español e inglés).
- **Vector embeddings** en tablas separadas 1:1, multi-modelo simultáneo (permite transición de modelos sin migración dura).
- **History** vía triggers `AFTER UPDATE/DELETE` a tablas `_history` que se replican con el sync.
- **Meta** (`_meta` table) con `schema_version` para migraciones futuras.
**Decisiones de diseño implícitas registradas**:
- IDs como ULID (sortable por tiempo, seguro para sync multi-device, URL-safe).
- Timestamps como `INTEGER` (epoch ms, portable).
- Hard delete con snapshot a `_history` antes (main table limpia, historia completa).
- Dimensión de vectores: **768 por default** (nomic-embed-text v1.5 vía Ollama — ver D-017). Nota: draft previa planteaba 1536 (OpenAI `text-embedding-3-small`), descartada al elegir provider local. Las columnas `model` y `dim` en las tablas de embeddings permiten convivencia multi-modelo; si se agrega un modelo con dimensión distinta hay que crear tabla espejo con `F32_BLOB(N)` apropiado.
- Columna `confidence` (`low` | `medium` | `high` | NULL) en `decisions` para marcar qué tan firme es la decisión al momento de escribirla. Útil para priorizar qué revisar: una decisión `low` con 6 meses sin tocarse es candidata a replantear; una `high` con `revisit_at` venciendo es alerta explícita. Queda nullable porque no todas las decisiones requieren esta anotación.
**Consecuencias y trabajo futuro**:
- Sintaxis de vectores LibSQL (`F32_BLOB`, `libsql_vector_idx`) a verificar contra versión específica al implementar.
- Falta índice compuesto `(project_id, updated_at DESC)` — agregar cuando se vean queries reales.
- Sin tabla de references externas (links a RFCs, papers, issues/commits) — se puede agregar en v2 si hace falta.
- Sin attachments (imágenes/PDFs) — se puede agregar en v2.
- Retención de history (D-025) pendiente de resolver antes de v1.0.

---

## D-015 — Formato y generación de IDs

**Estado**: Aceptada
**Contexto**: Definir el formato de IDs para todas las entidades. Crítico para sync multi-device: si la generación es server-side con autoincrement, inserts simultáneos en máquinas distintas colisionan al sincronizar.
**Decisión**: **ULID generados client-side** (en C#). 26 chars, sortables por tiempo, URL-safe, globalmente únicos sin coordinación.
**Alternativas consideradas**:
- *UUID v4*: único distribuido pero no sortable (importante para queries "últimas decisiones").
- *UUID v7*: sortable pero menos maduro en tooling; menor reconocimiento.
- *Autoincrement*: descartado por colisión garantizada en sync multi-device.
- *Nanoid*: único y compacto pero no sortable por tiempo.
- *Slug humano*: útil como alias secundario (ej: `brainz open "libsql-decision"`) pero no como PK.
**Consecuencias**:
- Sin `DEFAULT` en las columnas `id` del schema — el cliente C# genera el ULID antes del INSERT.
- Libs C# candidatas (a verificar AOT-friendly al implementar): `NetUlid`, `Ulid` de NuGet.
- Sortado cronológico gratis: `ORDER BY id` = `ORDER BY created_at` aproximadamente.
- Los IDs son copy-pasteables en URLs, CLI, logs sin escape.
- Opcional a futuro: campo `slug` secundario para URLs/comandos más legibles.

---

## D-017 — Provider de embeddings

**Estado**: Aceptada
**Contexto**: Decidir cómo se generan los embeddings para búsqueda semántica. Opciones evaluadas: API externa (OpenAI, Voyage, Cohere) vs modelo local (Ollama, llama.cpp, ONNX).
**Decisión**: **Ollama local con `nomic-embed-text` v1.5** como default. Dimensión **768**.
**Rationale**:
- **Offline first**: el brain funciona sin conexión, sin API keys, sin billing. Alineado con la filosofía OSS.
- **Privacidad**: contenido técnico personal nunca sale de la máquina.
- **nomic-embed-text v1.5 específicamente**: Apache 2.0 (OSS-friendly), multilingüe decente (maneja bien ES/EN/code), context window de 8192 tokens (cabe una decisión larga sin truncar), 274MB de descarga, dim 768 como sweet spot entre calidad y peso.
- **Ollama como runtime**: maduro, cross-platform, API HTTP local simple (`POST /api/embeddings`), cero fricción de instalación.
**Alternativas consideradas**:
- *OpenAI `text-embedding-3-small`* (dim 1536): mejor calidad marginal, pero requiere API key + billing + dependencia de red. Descartado para default.
- *Voyage AI*: excelente para código pero API externa + pago.
- *mxbai-embed-large* (dim 1024): mejor benchmark MTEB pero 669MB y más lento.
- *bge-small-en* (dim 384): liviano (134MB) pero solo inglés.
**Consecuencias**:
- Schema usa `F32_BLOB(768)` para todos los vector columns.
- Requisito de runtime: usuario debe tener Ollama instalado + modelo pulleado (`ollama pull nomic-embed-text`).
- El `brainz init` puede detectar Ollama y pullear el modelo automáticamente, o instruir cómo hacerlo.
- **Provider pluggable a futuro (D-023)**: la tabla `*_embeddings` con columnas `model` y `dim` permite convivir múltiples modelos/providers. Usuarios avanzados podrán configurar OpenAI/Voyage si prefieren.

---

## D-020 — Licencia

**Estado**: Aceptada
**Contexto**: Elección de licencia OSS para el proyecto. Se evaluó el trade-off entre simplicidad (MIT) y protección (Apache 2.0) considerando posibilidad de monetización futura.
**Decisión**: **Apache License 2.0**.
**Rationale**:
- **Concesión explícita de patentes**: Apache 2.0 incluye una cláusula de grant de patentes por parte de los contribuidores. MIT no menciona patentes, lo que deja gris la situación si un contribuidor aporta código relacionado a una patente que posee.
- **Cláusula de terminación de patentes**: si una parte demanda por patente al proyecto, pierde automáticamente los derechos de licencia que tenía. Actúa como disuasivo real.
- **Rastreabilidad mínima vía NOTICE**: quien redistribuya el código (modificado o no) debe mantener los avisos de copyright.
- **Viabilidad de monetización futura**: si el proyecto se vuelve rentable, Apache 2.0 permite construir servicio managed, features enterprise, o dual licensing sin necesidad de cambiar licencia del core (cambio que siempre es traumático y a menudo imposible sin CLA de contribuidores).
- **Familiaridad en el ecosistema**: Kubernetes, Terraform, Apache Kafka, Docker, Rust compiler, TensorFlow — todos Apache 2.0. Los contribuidores y adoptantes empresariales no tienen fricción.
**Alternativas consideradas**:
- *MIT*: más corta y simple, pero sin cobertura de patentes. Válida para proyectos sin ambición comercial o sin contribuidores externos. Descartada por la consideración pragmática de "si esto se llega a hacer rentable, capaz que nos sirve; si no sucede, no pasa nada" — Apache 2.0 agrega 180 líneas de `LICENSE` a cambio de mantener todas las puertas abiertas.
- *AGPL*: fuerza que quien hostee el software contribuya de vuelta. Descartada porque asusta a empresas que podrían usarlo internamente; el proyecto vive o muere por adopción.
- *BSL (Business Source License)*: modelo híbrido anti-cloud-providers. Overkill para un proyecto personal temprano.
**Archivos creados**:
- `LICENSE` — texto completo de Apache License 2.0 en root del repo.
- `NOTICE` — archivo mínimo con copyright y pointer a la licencia; se extenderá cuando haya dependencias que requieran notices.
**Convenciones adoptadas** (recomendadas por Apache, no obligatorias):
- Header de copyright en archivos fuente principales (opcional, se define al implementar).
- Campo `License` en el `.csproj` apuntando a "Apache-2.0".

---

## D-026 — Links polimórficos cross-entity

**Estado**: Aceptada
**Contexto**: La versión inicial del schema tenía `decision_links`, limitado a relaciones decision ↔ decision. En la práctica queremos expresar relaciones cross-entity: una *note* que informa una *decision*, un *principle* derivado de una *decision*, una *note* que relates_to otra *note*, etc.
**Alternativas consideradas**:
- *Tablas separadas por par* (`decision_principle_links`, `decision_note_links`...): explosión combinatoria, 9 tablas para 3 entidades.
- *FK polimórfica + triggers de integridad*: una sola tabla `links` con `from_type`, `from_id`, `to_type`, `to_id`. Sin FK automática pero con CHECK constraints y triggers que limpian al borrar.
- *Tabla genérica `entries` unificando todo*: perdíamos constraints específicos de cada entidad.
**Decisión**: Tabla única `links` polimórfica con `from_type/from_id` → `to_type/to_id` + `relation_type`. Integridad referencial mantenida con triggers `AFTER DELETE` en decisions/principles/notes que limpian links en ambas direcciones.
**Tipos de relación soportados**: `supersedes`, `relates_to`, `depends_on`, `conflicts_with`, `informed_by`, `derived_from` (para principles/notes nacidos de otra entidad), `split_from`, `contradicts` (para detección de inconsistencias).
**Consecuencias**:
- Pérdida de FK nativa compensada con CHECK constraints (`from_type`/`to_type` ∈ {decision, principle, note}) + triggers.
- El campo `promoted_from` que existía en la primera draft de `principles` queda **eliminado del schema** — la relación "principio nacido de decisión" se expresa via link `principle --derived_from--> decision`. (Estamos en fase de diseño, sin datos reales, por lo tanto eliminación directa sin migración.)
- Queries cross-entity simples: `SELECT * FROM links WHERE from_type='note' AND from_id=? AND relation_type='informed_by'`.
- Constraint anti-self-link global: `(from_type, from_id) != (to_type, to_id)`.

---

## D-027 — Tabla `project_remotes` (1:N)

**Estado**: Aceptada
**Contexto**: La versión inicial tenía `git_remote TEXT` inline en `projects`, asumiendo 1:1. En la práctica un proyecto puede tener múltiples remotes relevantes: fork propio, upstream oficial, mirror empresarial, o mono-repos con varios orígenes.
**Decisión**: Tabla `project_remotes` separada, 1:N con `projects`. Cada row tiene `remote_url` (único globalmente — un remote apunta a un solo proyecto) + `role` (`origin` | `upstream` | `fork` | `mirror` | `other`).
**Consecuencias**:
- Detección automática de scope: se matchea cualquier remote del repo cwd contra `remote_url`. Si hay múltiples, gana el primero encontrado.
- `brainz project add-remote <url> --role upstream` para registrar remotes adicionales.
- Un fork y su upstream pueden apuntar al mismo proyecto — el conocimiento viaja con el proyecto, no con el repo clonado.
- Unicidad global de `remote_url` previene ambigüedad: un remote solo puede pertenecer a un proyecto en el brain.

---

## D-013 — Front-end

**Estado**: Aceptada (implementación diferida a v1.0+)
**Contexto**: Un front-end web/desktop sería útil para visualizar el grafo de decisiones, editar contenido cómodo, y explorar relaciones. Pero no es crítico para v0.1 — el MCP + CLI alcanzan para validar el producto.
**Decisión**: **Postergar la implementación a v1.0 o posterior**, pero con el stack ya decidido para evitar re-discusión: **Next.js** (React + server-side rendering/routing, velocidad de desarrollo, ecosistema maduro). Se servirá localmente via `brainz serve` o como app standalone.
**Rationale de postergar**:
- MCP + CLI son la interfaz principal; el front es complementario.
- Implementar front antes de tener datos reales fluyendo es perder tiempo en UX especulativa.
- Next.js permite arrancar rápido cuando sea el momento (App Router, shadcn/ui, Tailwind son stack probado).
**Cuándo retomar**: cuando el uso real del CLI/MCP revele qué vistas son valiosas (probablemente grafo de decisiones linkeadas + timeline de history + dashboard de principles activos).

---

## D-014 — CLI como interfaz principal

**Estado**: Aceptada
**Contexto**: Se planteó originalmente como "CLI companion aparte del MCP". En la práctica, esa dualidad no tiene sentido.
**Decisión**: El CLI `brainz` **es** la interfaz principal del producto, no un complemento. CLI y MCP server son el **mismo binario**, compartiendo la misma lib interna (`Brainyz.Core`). El CLI expone comandos para humanos; el MCP expone las mismas operaciones como tools para agentes.
**Arquitectura resultante**:
```
brainyz/
├── Brainyz.Core/         # Lib: storage, search, scope, embeddings, models
├── Brainyz.Cli/          # CLI: System.CommandLine, invoca Core
└── Brainyz.Mcp/          # MCP server: expone Core via MCP SDK oficial
```
Modo de invocación:
- `brainz <command>` → CLI.
- `brainz mcp` → arranca el MCP server (stdio por default, TCP opcional).
**Consecuencias**:
- Cero duplicación de lógica entre CLI y MCP.
- Testeo uniforme contra `Core`.
- Un solo binario para distribuir.

---

## D-016 — Import / Export

**Estado**: Aceptada (para v0.1; ampliable)
**Contexto**: Usuarios van a querer respaldar, migrar entre máquinas, o compartir subsets del brain.
**Decisión para v0.1**: **Copiá el archivo `.db` entero**. Es la forma más robusta, completa, y operativamente simple. Incluye contenido + history + embeddings + índices en un solo file.
- `brainz export --to /path/brainyz-backup.db` — copia del archivo con VACUUM INTO.
- `brainz import --from /path/brainyz-backup.db` — reemplaza o mergea (a definir).
**Para v0.2+**: Export selectivo en JSON (decisiones filtradas por proyecto, tags, fechas). Útil para compartir subsets con equipo sin exponer todo.
**Rationale**: VACUUM INTO genera una copia optimizada del archivo SQLite/LibSQL, sin fragmentación, en una sola operación atómica. El archivo es portable y auto-contenido.
**Consecuencias**:
- Backup = copiar un archivo. Restore = reemplazar un archivo. Git versionado posible pero ineficiente por ser binario.
- Compatible con Litestream / rsync / cualquier herramienta estándar de backup.

---

## D-021 — Distribución y packaging

**Estado**: Aceptada
**Contexto**: Cómo llega el binario a los usuarios en cada plataforma.
**Decisión**: Roadmap escalonado de distribución según versión del proyecto.

**v0.1 — Source + GitHub Releases**:
- CI con matrix `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`.
- `dotnet publish -c Release -r <runtime>` con NativeAOT.
- Upload automático a GitHub Releases con tags `v0.1.x`.
- Usuarios bajan el binario y lo ponen en `PATH`.

**v0.2 — Package managers populares**:
- Homebrew formula (`brew install favitox/tap/brainyz`).
- Scoop bucket (Windows).

**v0.3 — Packaging extendido**:
- winget (Microsoft Store / Windows Package Manager).
- AUR (Arch User Repository).
- `nix` package si la comunidad lo pide.

**Rationale**:
- GitHub Releases es cero-fricción para el mantainer y universal para usuarios.
- Package managers llegan cuando el proyecto tiene uso real; antes es overhead de mantenimiento.
- Apple Notarization / Microsoft Signing se pospone hasta que haya tracción (proceso burocrático, costoso).

---

## D-023 — Extensibilidad / plugins

**Estado**: Aceptada (diferida a v2.0, con dirección definida)
**Contexto**: Otros devs/equipos van a querer extender brainyz: providers alternativos de embeddings (OpenAI, Voyage), scopes custom, integraciones con otras tools (Linear, Jira, Notion), etc.
**Decisión**: **Diferir implementación a v2.0**, pero con la arquitectura ya elegida: **subprocess/RPC language-agnostic**, patrón Terraform/kubectl plugins.
**Cómo va a funcionar**:
- Un plugin es un ejecutable cualquiera en `PATH` o directorio configurado, nombrado `brainz-<name>` (ej: `brainz-voyage-embeddings`).
- brainyz lo invoca por subprocess con JSON-RPC sobre stdin/stdout.
- El protocolo define los puntos de extensión (embedding provider, source plugin, notification hook, etc.).
**Rationale**:
- No lock-in de lenguaje: alguien puede escribir un plugin en Python, Rust, Go, lo que quiera.
- Compatible con NativeAOT (ver D-008): no dependemos de carga dinámica de assemblies.
- Patrón probado en el ecosistema OSS devtools (git, kubectl, gh CLI, terraform).
**Por qué v2.0 y no antes**:
- Diseñar una API de plugins sin casos de uso reales resulta en abstracciones equivocadas.
- Primero hay que ver qué extensiones pide la comunidad, después formalizarlas.
- Todo lo que hoy parecen plugins candidatos (providers de embeddings alternos, por ejemplo) se puede hardcodear como opción en v0/v1.

---

## Estado del diseño

**Todas las decisiones de diseño están cerradas.** Las decisiones restantes (retención de history D-025, refinamiento del front-end D-013, plugins D-023) están explícitamente diferidas a versiones futuras con dirección ya acordada.

**Backlog explícito para v1.0+**:
- D-013: Implementar front-end con Next.js (grafo + timeline + dashboard).
- D-025: Implementar retention policy de history cuando se acerque a 100MB.

**Backlog explícito para v2.0+**:
- D-023: API de plugins via subprocess/RPC estilo Terraform.
- D-016 (expansión): Export selectivo en JSON.

**De acá en adelante, todo es código.**

---

## Principios emergentes (candidatos a tabla `principles`)

1. **brainyz es la fuente; los proyectos son vistas.** Inverso al modelo de Engram.
2. **Registrar el "por qué no" tanto como el "por qué sí".** Las alternativas descartadas son lo más valioso a futuro.
3. **Simplicidad operativa.** Un archivo, un proceso, cero servicios. Backup = copiar.
4. **Portabilidad multiplataforma.** Nada que ate a un SO específico.
5. **Compatibilidad futura sin migración.** Decisiones de hoy no deben cerrar puertas obvias (ej: sync multi-device).

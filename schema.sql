-- ============================================================================
-- brainyz — Schema DDL v0.2
-- Target: LibSQL embebido (compatible SQLite)
-- Ubicación: ~/.config/brainyz/brainyz.db (Linux) / ~/Library/Application Support/brainyz/brainyz.db (Mac)
--
-- Convenciones:
--   - IDs: ULID generados client-side (C#) antes del INSERT. Sin DEFAULT en la columna.
--     Razón: sync multi-device requiere generación libre de colisión distribuida.
--   - Timestamps: INTEGER con epoch millis (unixepoch('subsec') * 1000).
--   - Contenido textual: TEXT con formato Markdown embebido.
--   - Embeddings: dimensión default 768 (nomic-embed-text v1.5 vía Ollama).
-- ============================================================================

PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;

-- ============================================================================
-- PROJECTS
-- Scoping opcional. NULL project_id = decisión global.
-- ============================================================================

CREATE TABLE projects (
    id              TEXT PRIMARY KEY,              -- ULID
    slug            TEXT NOT NULL UNIQUE,          -- 'ailang', 'brainyz', etc.
    name            TEXT NOT NULL,
    description     TEXT,
    created_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000),
    updated_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000)
);

CREATE INDEX idx_projects_slug ON projects(slug);

-- Un proyecto puede tener múltiples remotes: fork propio, upstream, mirror,
-- o mono-repo con varios orígenes. La detección automática de scope matchea
-- el remote del cwd contra cualquiera de los registrados.
CREATE TABLE project_remotes (
    id              TEXT PRIMARY KEY,              -- ULID
    project_id      TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    remote_url      TEXT NOT NULL,                 -- 'git@github.com:favio/ailang.git', etc.
    role            TEXT NOT NULL DEFAULT 'origin'
                    CHECK (role IN ('origin', 'upstream', 'fork', 'mirror', 'other')),
    created_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000),
    UNIQUE (remote_url)                            -- un remote_url mapea a un solo project
);

CREATE INDEX idx_project_remotes_project ON project_remotes(project_id);
CREATE INDEX idx_project_remotes_url ON project_remotes(remote_url);

-- ============================================================================
-- DECISIONS
-- Registro histórico contextual. Consultables on-demand.
-- ============================================================================

CREATE TABLE decisions (
    id              TEXT PRIMARY KEY,              -- ULID
    project_id      TEXT REFERENCES projects(id) ON DELETE SET NULL, -- NULL = global
    title           TEXT NOT NULL,
    status          TEXT NOT NULL DEFAULT 'accepted'
                    CHECK (status IN ('proposed', 'accepted', 'deprecated', 'superseded')),
    confidence      TEXT
                    CHECK (confidence IS NULL OR confidence IN ('low', 'medium', 'high')),

    -- Contenido en Markdown (todos los campos largos)
    context         TEXT,                          -- El problema/situación
    decision        TEXT NOT NULL,                 -- Qué se decidió
    rationale       TEXT,                          -- Por qué
    consequences    TEXT,                          -- Qué implica

    revisit_at      INTEGER,                       -- Unix ms; decisiones a replantear
    created_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000),
    updated_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000)
);

CREATE INDEX idx_decisions_project ON decisions(project_id);
CREATE INDEX idx_decisions_status ON decisions(status);
CREATE INDEX idx_decisions_updated ON decisions(updated_at DESC);
CREATE INDEX idx_decisions_revisit ON decisions(revisit_at) WHERE revisit_at IS NOT NULL;

-- ============================================================================
-- ALTERNATIVES
-- Las opciones descartadas. Lo más valioso a futuro.
-- ============================================================================

CREATE TABLE alternatives (
    id              TEXT PRIMARY KEY,              -- ULID
    decision_id     TEXT NOT NULL REFERENCES decisions(id) ON DELETE CASCADE,
    title           TEXT NOT NULL,
    description     TEXT,
    why_rejected    TEXT NOT NULL,
    sort_order      INTEGER NOT NULL DEFAULT 0,
    created_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000)
);

CREATE INDEX idx_alternatives_decision ON alternatives(decision_id);

-- ============================================================================
-- PRINCIPLES
-- Convicciones estables, se exponen al inicio de sesión Claude Code.
-- ============================================================================

CREATE TABLE principles (
    id              TEXT PRIMARY KEY,              -- ULID (generado client-side)
    project_id      TEXT REFERENCES projects(id) ON DELETE SET NULL, -- NULL = global
    title           TEXT NOT NULL,
    statement       TEXT NOT NULL,                 -- La afirmación en sí
    rationale       TEXT,                          -- Por qué
    active          INTEGER NOT NULL DEFAULT 1,    -- 0 = archivado
    created_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000),
    updated_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000)
);

-- Nota: La relación "principio nacido de una decisión" se expresa via
-- links: (principle) --derived_from--> (decision). Ver tabla `links`.

CREATE INDEX idx_principles_project ON principles(project_id);
CREATE INDEX idx_principles_active ON principles(active) WHERE active = 1;

-- ============================================================================
-- NOTES
-- Información descriptiva de referencia (no prescriptiva, no una decisión).
-- Ejemplos: "Stripe v2024-01 cambió refunds", "El equipo usa Linear, no Jira",
-- "La regex para CUIT es X", "Deploy de staging tarda ~8min".
-- ============================================================================

CREATE TABLE notes (
    id              TEXT PRIMARY KEY,              -- ULID
    project_id      TEXT REFERENCES projects(id) ON DELETE SET NULL, -- NULL = global
    title           TEXT NOT NULL,
    content         TEXT NOT NULL,                 -- Markdown
    source          TEXT,                          -- URL, documento, conversación donde surgió
    active          INTEGER NOT NULL DEFAULT 1,    -- 0 = archivado (info obsoleta pero queremos registro)
    created_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000),
    updated_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000)
);

CREATE INDEX idx_notes_project ON notes(project_id);
CREATE INDEX idx_notes_active ON notes(active) WHERE active = 1;
CREATE INDEX idx_notes_updated ON notes(updated_at DESC);

-- Tags para notes
CREATE TABLE note_tags (
    note_id         TEXT NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
    tag_id          TEXT NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
    PRIMARY KEY (note_id, tag_id)
);

CREATE INDEX idx_note_tags_tag ON note_tags(tag_id);

-- ============================================================================
-- TAGS
-- Jerárquicos via path ('ailang/auth', 'payments/idempotency').
-- ============================================================================

CREATE TABLE tags (
    id              TEXT PRIMARY KEY,              -- ULID
    path            TEXT NOT NULL UNIQUE,          -- 'ailang/auth', 'resilience'
    description     TEXT,
    created_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000)
);

CREATE INDEX idx_tags_path ON tags(path);

-- Many-to-many con decisions y principles
CREATE TABLE decision_tags (
    decision_id     TEXT NOT NULL REFERENCES decisions(id) ON DELETE CASCADE,
    tag_id          TEXT NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
    PRIMARY KEY (decision_id, tag_id)
);

CREATE INDEX idx_decision_tags_tag ON decision_tags(tag_id);

CREATE TABLE principle_tags (
    principle_id    TEXT NOT NULL REFERENCES principles(id) ON DELETE CASCADE,
    tag_id          TEXT NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
    PRIMARY KEY (principle_id, tag_id)
);

CREATE INDEX idx_principle_tags_tag ON principle_tags(tag_id);

-- ============================================================================
-- LINKS (polimórfico)
-- Grafo de relaciones cross-entity: decisions ↔ principles ↔ notes.
-- Ejemplos:
--   (decision) --supersedes-->  (decision)
--   (decision) --informed_by--> (note)
--   (principle) --derived_from--> (decision)
--   (note) --relates_to--> (decision)
-- Sin FK automática por ser polimórfico; CHECK constraint en tipos +
-- aplicación garantiza existencia al insertar.
-- ============================================================================

CREATE TABLE links (
    id              TEXT PRIMARY KEY,              -- ULID
    from_type       TEXT NOT NULL
                    CHECK (from_type IN ('decision', 'principle', 'note')),
    from_id         TEXT NOT NULL,
    to_type         TEXT NOT NULL
                    CHECK (to_type IN ('decision', 'principle', 'note')),
    to_id           TEXT NOT NULL,
    relation_type   TEXT NOT NULL
                    CHECK (relation_type IN (
                        'supersedes',       -- A reemplaza a B
                        'relates_to',       -- asociación general bidireccional
                        'depends_on',       -- A no tiene sentido sin B
                        'conflicts_with',   -- A y B entran en tensión
                        'informed_by',      -- A se tomó teniendo B en cuenta
                        'derived_from',     -- A salió/nació de B (ej: principle ← decision)
                        'split_from',       -- A se separó de B
                        'contradicts'       -- A va en contra de B (para detectar inconsistencias)
                    )),
    note            TEXT,                          -- contexto libre del link
    created_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000),
    UNIQUE (from_type, from_id, to_type, to_id, relation_type),
    CHECK (NOT (from_type = to_type AND from_id = to_id))  -- no self-links
);

CREATE INDEX idx_links_from ON links(from_type, from_id);
CREATE INDEX idx_links_to ON links(to_type, to_id);
CREATE INDEX idx_links_relation ON links(relation_type);

-- Triggers de integridad referencial polimórfica:
-- cuando se borra una entidad, borrar sus links (en ambas direcciones).
CREATE TRIGGER links_cleanup_on_decision_delete AFTER DELETE ON decisions BEGIN
    DELETE FROM links WHERE (from_type = 'decision' AND from_id = old.id)
                         OR (to_type = 'decision' AND to_id = old.id);
END;

CREATE TRIGGER links_cleanup_on_principle_delete AFTER DELETE ON principles BEGIN
    DELETE FROM links WHERE (from_type = 'principle' AND from_id = old.id)
                         OR (to_type = 'principle' AND to_id = old.id);
END;

CREATE TRIGGER links_cleanup_on_note_delete AFTER DELETE ON notes BEGIN
    DELETE FROM links WHERE (from_type = 'note' AND from_id = old.id)
                         OR (to_type = 'note' AND to_id = old.id);
END;

-- ============================================================================
-- EMBEDDINGS
-- Separadas en su propia tabla para permitir regenerar sin tocar el contenido,
-- y soportar múltiples modelos simultáneamente (ej: transición de modelo).
-- Dimensión default: 768 (nomic-embed-text v1.5 vía Ollama). Si se agrega otro
-- modelo con dimensión distinta, crear tabla espejo con F32_BLOB apropiado.
-- ============================================================================

-- Para decisions
CREATE TABLE decision_embeddings (
    decision_id     TEXT NOT NULL REFERENCES decisions(id) ON DELETE CASCADE,
    model           TEXT NOT NULL,                 -- 'nomic-embed-text:v1.5', 'bge-small-en', etc.
    dim             INTEGER NOT NULL,              -- 768 para nomic, 384 para bge-small, etc.
    vector          F32_BLOB(768) NOT NULL,        -- LibSQL vector type; tabla asume dim=768
    content_hash    TEXT NOT NULL,                 -- hash del contenido que generó este embedding
    created_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000),
    PRIMARY KEY (decision_id, model)
);

-- Para principles
CREATE TABLE principle_embeddings (
    principle_id    TEXT NOT NULL REFERENCES principles(id) ON DELETE CASCADE,
    model           TEXT NOT NULL,
    dim             INTEGER NOT NULL,
    vector          F32_BLOB(768) NOT NULL,
    content_hash    TEXT NOT NULL,
    created_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000),
    PRIMARY KEY (principle_id, model)
);

-- Para notes
CREATE TABLE note_embeddings (
    note_id         TEXT NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
    model           TEXT NOT NULL,
    dim             INTEGER NOT NULL,
    vector          F32_BLOB(768) NOT NULL,
    content_hash    TEXT NOT NULL,
    created_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000),
    PRIMARY KEY (note_id, model)
);

-- Índices vectoriales (LibSQL syntax)
CREATE INDEX idx_decision_embeddings_vec ON decision_embeddings(libsql_vector_idx(vector));
CREATE INDEX idx_principle_embeddings_vec ON principle_embeddings(libsql_vector_idx(vector));
CREATE INDEX idx_note_embeddings_vec ON note_embeddings(libsql_vector_idx(vector));

-- ============================================================================
-- FULL-TEXT SEARCH (FTS5)
-- Para matches literales. Se combina con vector search en el MCP.
-- ============================================================================

CREATE VIRTUAL TABLE decisions_fts USING fts5(
    decision_id UNINDEXED,
    title,
    context,
    decision,
    rationale,
    tokenize = 'unicode61 remove_diacritics 2'
);

CREATE VIRTUAL TABLE principles_fts USING fts5(
    principle_id UNINDEXED,
    title,
    statement,
    rationale,
    tokenize = 'unicode61 remove_diacritics 2'
);

CREATE VIRTUAL TABLE notes_fts USING fts5(
    note_id UNINDEXED,
    title,
    content,
    source,
    tokenize = 'unicode61 remove_diacritics 2'
);

-- Triggers de sincronización decisions -> decisions_fts
CREATE TRIGGER decisions_fts_insert AFTER INSERT ON decisions BEGIN
    INSERT INTO decisions_fts (decision_id, title, context, decision, rationale)
    VALUES (new.id, new.title, new.context, new.decision, new.rationale);
END;

CREATE TRIGGER decisions_fts_update AFTER UPDATE ON decisions BEGIN
    UPDATE decisions_fts SET
        title = new.title,
        context = new.context,
        decision = new.decision,
        rationale = new.rationale
    WHERE decision_id = new.id;
END;

CREATE TRIGGER decisions_fts_delete AFTER DELETE ON decisions BEGIN
    DELETE FROM decisions_fts WHERE decision_id = old.id;
END;

-- Triggers para principles
CREATE TRIGGER principles_fts_insert AFTER INSERT ON principles BEGIN
    INSERT INTO principles_fts (principle_id, title, statement, rationale)
    VALUES (new.id, new.title, new.statement, new.rationale);
END;

CREATE TRIGGER principles_fts_update AFTER UPDATE ON principles BEGIN
    UPDATE principles_fts SET
        title = new.title,
        statement = new.statement,
        rationale = new.rationale
    WHERE principle_id = new.id;
END;

CREATE TRIGGER principles_fts_delete AFTER DELETE ON principles BEGIN
    DELETE FROM principles_fts WHERE principle_id = old.id;
END;

-- Triggers para notes
CREATE TRIGGER notes_fts_insert AFTER INSERT ON notes BEGIN
    INSERT INTO notes_fts (note_id, title, content, source)
    VALUES (new.id, new.title, new.content, new.source);
END;

CREATE TRIGGER notes_fts_update AFTER UPDATE ON notes BEGIN
    UPDATE notes_fts SET
        title = new.title,
        content = new.content,
        source = new.source
    WHERE note_id = new.id;
END;

CREATE TRIGGER notes_fts_delete AFTER DELETE ON notes BEGIN
    DELETE FROM notes_fts WHERE note_id = old.id;
END;

-- ============================================================================
-- HISTORY (audit log)
-- Triggers AFTER UPDATE/DELETE copian la fila previa a tabla espejo.
-- Se replica vía sync LibSQL junto con el resto.
-- ============================================================================

CREATE TABLE decisions_history (
    history_id      INTEGER PRIMARY KEY AUTOINCREMENT,
    decision_id     TEXT NOT NULL,
    change_type     TEXT NOT NULL CHECK (change_type IN ('update', 'delete')),
    changed_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000),
    -- snapshot de la fila anterior
    project_id      TEXT,
    title           TEXT,
    status          TEXT,
    confidence      TEXT,
    context         TEXT,
    decision        TEXT,
    rationale       TEXT,
    consequences    TEXT,
    revisit_at      INTEGER,
    created_at      INTEGER,
    updated_at      INTEGER
);

CREATE INDEX idx_decisions_history_id ON decisions_history(decision_id, changed_at DESC);

CREATE TRIGGER decisions_history_update AFTER UPDATE ON decisions BEGIN
    INSERT INTO decisions_history (
        decision_id, change_type, project_id, title, status, confidence,
        context, decision, rationale, consequences, revisit_at, created_at, updated_at
    ) VALUES (
        old.id, 'update', old.project_id, old.title, old.status, old.confidence,
        old.context, old.decision, old.rationale, old.consequences, old.revisit_at,
        old.created_at, old.updated_at
    );
END;

CREATE TRIGGER decisions_history_delete AFTER DELETE ON decisions BEGIN
    INSERT INTO decisions_history (
        decision_id, change_type, project_id, title, status, confidence,
        context, decision, rationale, consequences, revisit_at, created_at, updated_at
    ) VALUES (
        old.id, 'delete', old.project_id, old.title, old.status, old.confidence,
        old.context, old.decision, old.rationale, old.consequences, old.revisit_at,
        old.created_at, old.updated_at
    );
END;

-- History para principles
CREATE TABLE principles_history (
    history_id      INTEGER PRIMARY KEY AUTOINCREMENT,
    principle_id    TEXT NOT NULL,
    change_type     TEXT NOT NULL CHECK (change_type IN ('update', 'delete')),
    changed_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000),
    project_id      TEXT,
    title           TEXT,
    statement       TEXT,
    rationale       TEXT,
    active          INTEGER,
    created_at      INTEGER,
    updated_at      INTEGER
);

CREATE INDEX idx_principles_history_id ON principles_history(principle_id, changed_at DESC);

CREATE TRIGGER principles_history_update AFTER UPDATE ON principles BEGIN
    INSERT INTO principles_history (
        principle_id, change_type, project_id, title, statement, rationale,
        active, created_at, updated_at
    ) VALUES (
        old.id, 'update', old.project_id, old.title, old.statement, old.rationale,
        old.active, old.created_at, old.updated_at
    );
END;

CREATE TRIGGER principles_history_delete AFTER DELETE ON principles BEGIN
    INSERT INTO principles_history (
        principle_id, change_type, project_id, title, statement, rationale,
        active, created_at, updated_at
    ) VALUES (
        old.id, 'delete', old.project_id, old.title, old.statement, old.rationale,
        old.active, old.created_at, old.updated_at
    );
END;

-- History para notes
CREATE TABLE notes_history (
    history_id      INTEGER PRIMARY KEY AUTOINCREMENT,
    note_id         TEXT NOT NULL,
    change_type     TEXT NOT NULL CHECK (change_type IN ('update', 'delete')),
    changed_at      INTEGER NOT NULL DEFAULT (unixepoch('subsec') * 1000),
    project_id      TEXT,
    title           TEXT,
    content         TEXT,
    source          TEXT,
    active          INTEGER,
    created_at      INTEGER,
    updated_at      INTEGER
);

CREATE INDEX idx_notes_history_id ON notes_history(note_id, changed_at DESC);

CREATE TRIGGER notes_history_update AFTER UPDATE ON notes BEGIN
    INSERT INTO notes_history (
        note_id, change_type, project_id, title, content, source,
        active, created_at, updated_at
    ) VALUES (
        old.id, 'update', old.project_id, old.title, old.content, old.source,
        old.active, old.created_at, old.updated_at
    );
END;

CREATE TRIGGER notes_history_delete AFTER DELETE ON notes BEGIN
    INSERT INTO notes_history (
        note_id, change_type, project_id, title, content, source,
        active, created_at, updated_at
    ) VALUES (
        old.id, 'delete', old.project_id, old.title, old.content, old.source,
        old.active, old.created_at, old.updated_at
    );
END;

-- ============================================================================
-- TRIGGER: updated_at automático
-- ============================================================================

CREATE TRIGGER decisions_set_updated_at AFTER UPDATE ON decisions
WHEN old.updated_at = new.updated_at
BEGIN
    UPDATE decisions SET updated_at = unixepoch('subsec') * 1000 WHERE id = new.id;
END;

CREATE TRIGGER principles_set_updated_at AFTER UPDATE ON principles
WHEN old.updated_at = new.updated_at
BEGIN
    UPDATE principles SET updated_at = unixepoch('subsec') * 1000 WHERE id = new.id;
END;

CREATE TRIGGER notes_set_updated_at AFTER UPDATE ON notes
WHEN old.updated_at = new.updated_at
BEGIN
    UPDATE notes SET updated_at = unixepoch('subsec') * 1000 WHERE id = new.id;
END;

CREATE TRIGGER projects_set_updated_at AFTER UPDATE ON projects
WHEN old.updated_at = new.updated_at
BEGIN
    UPDATE projects SET updated_at = unixepoch('subsec') * 1000 WHERE id = new.id;
END;

-- ============================================================================
-- META: schema version
-- ============================================================================

CREATE TABLE _meta (
    key             TEXT PRIMARY KEY,
    value           TEXT NOT NULL
);

INSERT INTO _meta (key, value) VALUES
    ('schema_version', '1'),
    ('created_at', CAST(unixepoch('subsec') * 1000 AS TEXT));

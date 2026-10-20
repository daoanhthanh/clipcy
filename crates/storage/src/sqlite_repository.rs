use std::path::PathBuf;
use std::str::FromStr;

use async_trait::async_trait;
use sqlx::Row;
use sqlx::sqlite::{SqliteConnectOptions, SqlitePool, SqlitePoolOptions};
use uuid::Uuid;

use clipboard_core::domain::{ClipboardItem, ContentType, normalize_search_text};
use clipboard_core::error::{ClipboardError, Result};
use clipboard_core::ports::ClipboardRepository;

/// Calculate a simple hash of content for deduplication
fn calculate_content_hash(data: &[u8]) -> String {
    use std::collections::hash_map::DefaultHasher;
    use std::hash::{Hash, Hasher};

    let mut hasher = DefaultHasher::new();
    data.hash(&mut hasher);
    format!("{:016x}", hasher.finish())
}

const FTS_ORIGINAL_COLUMN: &str = "search_text";
const FTS_NORMALIZED_COLUMN: &str = "search_text_norm";

#[derive(Clone)]
struct QueryToken {
    original: String,
    normalized: String,
}

fn tokenize_query(query: &str) -> Vec<String> {
    query
        .split(|c: char| !c.is_alphanumeric())
        .filter(|part| !part.is_empty())
        .map(|part| part.to_ascii_lowercase())
        .collect()
}

fn tokenize_query_pairs(query: &str) -> Vec<QueryToken> {
    tokenize_query(query)
        .into_iter()
        .map(|token| {
            let normalized = normalize_search_text(&token);
            QueryToken {
                original: token,
                normalized,
            }
        })
        .collect()
}

fn build_column_term(token: &str, column: &str) -> Option<String> {
    let trimmed = token.trim();
    if trimmed.is_empty() {
        return None;
    }

    let term = if trimmed.len() >= 2 {
        format!("{}*", trimmed)
    } else {
        trimmed.to_string()
    };

    Some(format!("{}:{}", column, term))
}

fn build_match_term(token: &QueryToken) -> Option<String> {
    let original = build_column_term(&token.original, FTS_ORIGINAL_COLUMN);
    let normalized = build_column_term(&token.normalized, FTS_NORMALIZED_COLUMN);

    match (original, normalized) {
        (Some(original), Some(normalized)) => Some(format!("({} OR {})", original, normalized)),
        (Some(original), None) => Some(original),
        (None, Some(normalized)) => Some(normalized),
        (None, None) => None,
    }
}

fn build_match_terms(tokens: &[QueryToken]) -> Vec<String> {
    tokens.iter().filter_map(build_match_term).collect()
}

fn compact_query(query: &str) -> String {
    query
        .chars()
        .filter(|c| c.is_alphanumeric())
        .collect::<String>()
        .to_ascii_lowercase()
}

fn token_len(token: &QueryToken) -> usize {
    token.original.len().max(token.normalized.len())
}

fn query_has_diacritics(query: &str) -> bool {
    normalize_search_text(query) != query.to_ascii_lowercase()
}

fn build_fts_queries(query: &str) -> Vec<String> {
    let tokens = tokenize_query_pairs(query);
    if tokens.is_empty() {
        return Vec::new();
    }

    let strict_terms = build_match_terms(&tokens);
    let mut queries = Vec::new();
    if !strict_terms.is_empty() {
        queries.push(strict_terms.join(" AND "));
    }

    let relaxed_tokens: Vec<QueryToken> = tokens
        .iter()
        .filter(|token| token_len(token) >= 3)
        .cloned()
        .collect();
    let base_tokens = if relaxed_tokens.is_empty() {
        tokens.clone()
    } else {
        relaxed_tokens
    };
    let mut relaxed_terms = build_match_terms(&base_tokens);

    let compact = compact_query(query);
    let compact_normalized = compact_query(&normalize_search_text(query));
    let has_compact = base_tokens
        .iter()
        .any(|token| token.original == compact || token.normalized == compact_normalized);
    if compact.len() >= 3 && !has_compact {
        let compact_token = QueryToken {
            original: compact,
            normalized: compact_normalized,
        };
        if let Some(term) = build_match_term(&compact_token) {
            relaxed_terms.push(term);
        }
    }

    if !relaxed_terms.is_empty() {
        let relaxed = relaxed_terms.join(" OR ");
        if queries.last().map(|q| q != &relaxed).unwrap_or(true) {
            queries.push(relaxed);
        }
    }

    queries
}

/// SQLite-based clipboard repository using sqlx for truly async operations
pub struct SqliteClipboardRepository {
    pool: SqlitePool,
}

impl SqliteClipboardRepository {
    /// Create new repository with database at specified path
    pub async fn new(path: PathBuf) -> Result<Self> {
        let path_str = path.to_string_lossy().to_string();

        let options = SqliteConnectOptions::from_str(&format!("sqlite://{}?mode=rwc", path_str))
            .map_err(|e| ClipboardError::Storage(e.to_string()))?
            .create_if_missing(true);

        let pool = SqlitePoolOptions::new()
            .min_connections(0)
            .max_connections(1)
            .connect_with(options)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        let repo = Self { pool };
        repo.init_schema().await?;
        Ok(repo)
    }

    /// Create repository with default database location
    pub async fn default_location() -> Result<Self> {
        let data_dir = dirs::data_local_dir()
            .ok_or_else(|| ClipboardError::Storage("Cannot find data directory".to_string()))?;

        let app_dir = data_dir.join("lipcy");
        std::fs::create_dir_all(&app_dir)?;

        let db_path = app_dir.join("clipcy.db");
        Self::new(db_path).await
    }

    async fn init_schema(&self) -> Result<()> {
        sqlx::query(
            r#"
            CREATE TABLE IF NOT EXISTS clipboard_items (
                id TEXT PRIMARY KEY,
                content_type TEXT NOT NULL,
                data BLOB NOT NULL,
                preview TEXT NOT NULL,
                search_text TEXT,
                search_text_norm TEXT,
                thumbnail BLOB,
                source_app TEXT,
                source_app_icon BLOB,
                created_at TEXT NOT NULL,
                last_used_at TEXT NOT NULL,
                pinned INTEGER NOT NULL DEFAULT 0,
                pinned_at TEXT,
                content_hash TEXT
            )
            "#,
        )
        .execute(&self.pool)
        .await
        .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        sqlx::query("CREATE INDEX IF NOT EXISTS idx_pinned ON clipboard_items(pinned)")
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        // Index for deduplication lookup
        sqlx::query("CREATE INDEX IF NOT EXISTS idx_content_hash ON clipboard_items(content_hash)")
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        // Add content_hash column if it doesn't exist (for existing databases)
        let _ = sqlx::query("ALTER TABLE clipboard_items ADD COLUMN content_hash TEXT")
            .execute(&self.pool)
            .await;

        let _ = sqlx::query("ALTER TABLE clipboard_items ADD COLUMN source_app_icon BLOB")
            .execute(&self.pool)
            .await;

        let _ = sqlx::query("ALTER TABLE clipboard_items ADD COLUMN search_text TEXT")
            .execute(&self.pool)
            .await;

        let _ = sqlx::query("ALTER TABLE clipboard_items ADD COLUMN search_text_norm TEXT")
            .execute(&self.pool)
            .await;

        let _ = sqlx::query("ALTER TABLE clipboard_items ADD COLUMN last_used_at TEXT")
            .execute(&self.pool)
            .await;

        let _ = sqlx::query(
            "UPDATE clipboard_items SET last_used_at = created_at WHERE last_used_at IS NULL OR last_used_at = ''",
        )
        .execute(&self.pool)
        .await;

        sqlx::query(
            "CREATE INDEX IF NOT EXISTS idx_last_used_at ON clipboard_items(last_used_at DESC)",
        )
        .execute(&self.pool)
        .await
        .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        let columns = sqlx::query("PRAGMA table_info(clipboard_items)")
            .fetch_all(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        if !columns
            .iter()
            .any(|row| row.get::<String, _>("name") == "pinned_at")
        {
            sqlx::query("ALTER TABLE clipboard_items ADD COLUMN pinned_at TEXT")
                .execute(&self.pool)
                .await
                .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        }
        // Older databases did not record pin time; retain their current order as a fallback.
        sqlx::query("UPDATE clipboard_items SET pinned_at = last_used_at WHERE pinned != 0 AND pinned_at IS NULL")
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        self.init_fts().await?;
        self.backfill_search_text().await?;
        self.rebuild_fts().await?;

        Ok(())
    }

    async fn init_fts(&self) -> Result<()> {
        let fts_columns = sqlx::query("PRAGMA table_info(clipboard_items_fts)")
            .fetch_all(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let has_normalized = fts_columns.iter().any(|row| {
            row.try_get::<String, _>("name")
                .map(|name| name == FTS_NORMALIZED_COLUMN)
                .unwrap_or(false)
        });

        sqlx::query("DROP TRIGGER IF EXISTS clipboard_items_ai")
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        sqlx::query("DROP TRIGGER IF EXISTS clipboard_items_ad")
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        sqlx::query("DROP TRIGGER IF EXISTS clipboard_items_au")
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        if !fts_columns.is_empty() && !has_normalized {
            sqlx::query("DROP TABLE IF EXISTS clipboard_items_fts")
                .execute(&self.pool)
                .await
                .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        }

        sqlx::query(
            r#"
            CREATE VIRTUAL TABLE IF NOT EXISTS clipboard_items_fts
            USING fts5(
                search_text,
                search_text_norm,
                content='clipboard_items',
                content_rowid='rowid',
                tokenize='unicode61 remove_diacritics 0'
            )
            "#,
        )
        .execute(&self.pool)
        .await
        .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        sqlx::query(
            r#"
            CREATE TRIGGER IF NOT EXISTS clipboard_items_ai
            AFTER INSERT ON clipboard_items BEGIN
                INSERT INTO clipboard_items_fts(rowid, search_text, search_text_norm)
                VALUES (new.rowid, new.search_text, new.search_text_norm);
            END;
            "#,
        )
        .execute(&self.pool)
        .await
        .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        sqlx::query(
            r#"
            CREATE TRIGGER IF NOT EXISTS clipboard_items_ad
            AFTER DELETE ON clipboard_items BEGIN
                INSERT INTO clipboard_items_fts(clipboard_items_fts, rowid, search_text, search_text_norm)
                VALUES ('delete', old.rowid, old.search_text, old.search_text_norm);
            END;
            "#,
        )
        .execute(&self.pool)
        .await
        .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        sqlx::query(
            r#"
            CREATE TRIGGER IF NOT EXISTS clipboard_items_au
            AFTER UPDATE ON clipboard_items BEGIN
                INSERT INTO clipboard_items_fts(clipboard_items_fts, rowid, search_text, search_text_norm)
                VALUES ('delete', old.rowid, old.search_text, old.search_text_norm);
                INSERT INTO clipboard_items_fts(rowid, search_text, search_text_norm)
                VALUES (new.rowid, new.search_text, new.search_text_norm);
            END;
            "#,
        )
        .execute(&self.pool)
        .await
        .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        Ok(())
    }

    async fn rebuild_fts(&self) -> Result<()> {
        sqlx::query("INSERT INTO clipboard_items_fts(clipboard_items_fts) VALUES ('rebuild')")
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        Ok(())
    }

    async fn backfill_search_text(&self) -> Result<()> {
        let rows = sqlx::query("SELECT * FROM clipboard_items")
            .fetch_all(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        for row in rows {
            let item = Self::row_to_item(&row)?;
            let (search_text, search_text_norm) = item.searchable_texts();
            let current_search_text: Option<String> = row
                .try_get("search_text")
                .map_err(|e| ClipboardError::Storage(e.to_string()))?;
            let current_search_text_norm: Option<String> = row
                .try_get("search_text_norm")
                .map_err(|e| ClipboardError::Storage(e.to_string()))?;
            if current_search_text.as_deref() == Some(&search_text)
                && current_search_text_norm.as_deref() == Some(&search_text_norm)
            {
                continue;
            }

            sqlx::query(
                "UPDATE clipboard_items SET search_text = $1, search_text_norm = $2 WHERE id = $3",
            )
            .bind(search_text)
            .bind(search_text_norm)
            .bind(item.id.to_string())
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        }

        Ok(())
    }

    fn row_to_item(row: &sqlx::sqlite::SqliteRow) -> Result<ClipboardItem> {
        let id_str: String = row
            .try_get("id")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let content_type_str: String = row
            .try_get("content_type")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let data: Vec<u8> = row
            .try_get("data")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let preview: String = row
            .try_get("preview")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let thumbnail: Option<Vec<u8>> = row
            .try_get("thumbnail")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let source_app: Option<String> = row
            .try_get("source_app")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let source_app_icon: Option<Vec<u8>> = row
            .try_get("source_app_icon")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let created_at_str: String = row
            .try_get("created_at")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let last_used_at_str: String = row
            .try_get("last_used_at")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let pinned: i32 = row
            .try_get("pinned")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        let content_type: ContentType = serde_json::from_str(&content_type_str).unwrap_or_default();

        let created_at = chrono::DateTime::parse_from_rfc3339(&created_at_str)
            .map(|dt| dt.with_timezone(&chrono::Utc))
            .unwrap_or_default();
        let last_used_at = chrono::DateTime::parse_from_rfc3339(&last_used_at_str)
            .map(|dt| dt.with_timezone(&chrono::Utc))
            .unwrap_or(created_at);

        Ok(ClipboardItem {
            id: Uuid::parse_str(&id_str).unwrap_or_default(),
            content_type,
            data,
            preview,
            thumbnail,
            source_app,
            source_app_icon,
            created_at,
            last_used_at,
            pinned: pinned != 0,
        })
    }

    fn row_to_item_summary(row: &sqlx::sqlite::SqliteRow) -> Result<ClipboardItem> {
        let id_str: String = row
            .try_get("id")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let content_type_str: String = row
            .try_get("content_type")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let preview: String = row
            .try_get("preview")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let thumbnail: Option<Vec<u8>> = row
            .try_get("thumbnail")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let source_app: Option<String> = row
            .try_get("source_app")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let source_app_icon: Option<Vec<u8>> = row
            .try_get("source_app_icon")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let created_at_str: String = row
            .try_get("created_at")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let last_used_at_str: String = row
            .try_get("last_used_at")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let pinned: i32 = row
            .try_get("pinned")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        let content_type: ContentType = serde_json::from_str(&content_type_str).unwrap_or_default();

        let created_at = chrono::DateTime::parse_from_rfc3339(&created_at_str)
            .map(|dt| dt.with_timezone(&chrono::Utc))
            .unwrap_or_default();
        let last_used_at = chrono::DateTime::parse_from_rfc3339(&last_used_at_str)
            .map(|dt| dt.with_timezone(&chrono::Utc))
            .unwrap_or(created_at);

        Ok(ClipboardItem {
            id: Uuid::parse_str(&id_str).unwrap_or_default(),
            content_type,
            data: Vec::new(),
            preview,
            thumbnail,
            source_app,
            source_app_icon,
            created_at,
            last_used_at,
            pinned: pinned != 0,
        })
    }

    pub async fn get_all_metadata(&self, limit: usize) -> Result<Vec<ClipboardItem>> {
        let rows = sqlx::query(
            "SELECT id, content_type, preview, thumbnail, source_app, source_app_icon, created_at, last_used_at, pinned FROM clipboard_items ORDER BY pinned DESC, CASE WHEN pinned != 0 THEN pinned_at ELSE last_used_at END DESC, id ASC LIMIT ($1 + (SELECT COUNT(*) FROM clipboard_items WHERE pinned != 0))"
        )
            .bind(limit as i64)
            .fetch_all(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        let mut items = Vec::with_capacity(rows.len());
        for row in rows {
            if let Ok(item) = Self::row_to_item_summary(&row) {
                items.push(item);
            }
        }

        Ok(items)
    }

    pub async fn search_metadata(&self, query: &str, limit: usize) -> Result<Vec<ClipboardItem>> {
        if query.trim().is_empty() {
            return self.get_all_metadata(limit).await;
        }

        self.search_with_fts(query, limit, true).await
    }

    async fn search_with_fts(
        &self,
        query: &str,
        limit: usize,
        metadata_only: bool,
    ) -> Result<Vec<ClipboardItem>> {
        let queries = build_fts_queries(query);
        if queries.is_empty() {
            return Ok(Vec::new());
        }

        let (weight_original, weight_normalized) = if query_has_diacritics(query) {
            (2.0_f64, 1.0_f64)
        } else {
            (1.0_f64, 2.0_f64)
        };

        for fts_query in queries {
            let rows = if metadata_only {
                sqlx::query(
                    r#"
                    SELECT ci.id, ci.content_type, ci.preview, ci.thumbnail, ci.source_app, ci.source_app_icon, ci.created_at, ci.last_used_at, ci.pinned
                    FROM clipboard_items ci
                    JOIN clipboard_items_fts ON ci.rowid = clipboard_items_fts.rowid
                    WHERE clipboard_items_fts MATCH $1
                    ORDER BY ci.pinned DESC, CASE WHEN ci.pinned != 0 THEN ci.pinned_at END DESC, bm25(clipboard_items_fts, $3, $4), CASE WHEN ci.pinned = 0 THEN ci.last_used_at END DESC, ci.id ASC
                    LIMIT $2
                    "#,
                )
                .bind(&fts_query)
                .bind(limit as i64)
                .bind(weight_original)
                .bind(weight_normalized)
                .fetch_all(&self.pool)
                .await
                .map_err(|e| ClipboardError::Storage(e.to_string()))?
            } else {
                sqlx::query(
                    r#"
                    SELECT ci.*
                    FROM clipboard_items ci
                    JOIN clipboard_items_fts ON ci.rowid = clipboard_items_fts.rowid
                    WHERE clipboard_items_fts MATCH $1
                    ORDER BY ci.pinned DESC, CASE WHEN ci.pinned != 0 THEN ci.pinned_at END DESC, bm25(clipboard_items_fts, $3, $4), CASE WHEN ci.pinned = 0 THEN ci.last_used_at END DESC, ci.id ASC
                    LIMIT $2
                    "#,
                )
                .bind(&fts_query)
                .bind(limit as i64)
                .bind(weight_original)
                .bind(weight_normalized)
                .fetch_all(&self.pool)
                .await
                .map_err(|e| ClipboardError::Storage(e.to_string()))?
            };

            if rows.is_empty() {
                continue;
            }

            let mut items = Vec::with_capacity(rows.len());
            for row in rows {
                let item = if metadata_only {
                    Self::row_to_item_summary(&row)
                } else {
                    Self::row_to_item(&row)
                };

                if let Ok(item) = item {
                    items.push(item);
                }
            }

            if !items.is_empty() {
                return Ok(items);
            }
        }

        Ok(Vec::new())
    }
}

#[async_trait]
impl ClipboardRepository for SqliteClipboardRepository {
    async fn save(&self, item: &ClipboardItem) -> Result<()> {
        tracing::debug!(
            id = %item.id,
            content_type = ?item.content_type,
            data_len = item.data.len(),
            preview_len = item.preview.len(),
            thumbnail_len = item.thumbnail.as_ref().map(|t| t.len()).unwrap_or(0),
            source_app = item.source_app.as_deref(),
            "Saving clipboard item to database"
        );
        let content_type = serde_json::to_string(&item.content_type)?;
        let content_hash = calculate_content_hash(&item.data);
        let (search_text, search_text_norm) = item.searchable_texts();
        let mut pinned_to_save = item.pinned;
        let mut id_to_save = item.id.to_string();

        // Only deduplicate text content - images should always be kept as separate entries
        if item.content_type == ContentType::Text {
            if let Some(row) = sqlx::query(
                "SELECT id, pinned FROM clipboard_items WHERE content_hash = $1 ORDER BY pinned DESC LIMIT 1",
            )
            .bind(&content_hash)
            .fetch_optional(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?
            {
                let existing_pinned: i32 = row
                    .try_get("pinned")
                    .map_err(|e| ClipboardError::Storage(e.to_string()))?;
                if existing_pinned != 0 {
                    pinned_to_save = true;
                    // Pinned shortcuts refer to identity, even when the text is copied again.
                    id_to_save = row
                        .try_get("id")
                        .map_err(|e| ClipboardError::Storage(e.to_string()))?;
                }
            }

            // Delete any existing item with the same content hash (deduplication)
            // This removes the old entry so the new one will be at the top when sorted by last_used_at
            sqlx::query("DELETE FROM clipboard_items WHERE content_hash = $1 AND id != $2")
                .bind(&content_hash)
                .bind(&id_to_save)
                .execute(&self.pool)
                .await
                .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        }

        sqlx::query(
            r#"
            INSERT OR REPLACE INTO clipboard_items
            (id, content_type, data, preview, search_text, search_text_norm, thumbnail, source_app, source_app_icon, created_at, last_used_at, pinned, content_hash, pinned_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13,
                CASE WHEN $12 != 0 THEN COALESCE(
                    (SELECT pinned_at FROM clipboard_items WHERE id = $1 AND pinned != 0), $14) END)
            "#,
        )
            .bind(&id_to_save)
            .bind(content_type)
            .bind(&item.data)
            .bind(&item.preview)
            .bind(&search_text)
            .bind(&search_text_norm)
            .bind(&item.thumbnail)
            .bind(&item.source_app)
            .bind(&item.source_app_icon)
            .bind(item.created_at.to_rfc3339())
            .bind(item.last_used_at.to_rfc3339())
            .bind(pinned_to_save as i32)
            .bind(&content_hash)
            .bind(chrono::Utc::now().to_rfc3339())
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        tracing::debug!(id = %item.id, "Clipboard item saved to database");

        Ok(())
    }

    async fn get_by_id(&self, id: Uuid) -> Result<Option<ClipboardItem>> {
        let row = sqlx::query("SELECT * FROM clipboard_items WHERE id = $1")
            .bind(id.to_string())
            .fetch_optional(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        match row {
            Some(r) => Ok(Some(Self::row_to_item(&r)?)),
            None => Ok(None),
        }
    }

    async fn get_all(&self, limit: usize) -> Result<Vec<ClipboardItem>> {
        let rows = sqlx::query(
            "SELECT * FROM clipboard_items ORDER BY pinned DESC, CASE WHEN pinned != 0 THEN pinned_at ELSE last_used_at END DESC, id ASC LIMIT ($1 + (SELECT COUNT(*) FROM clipboard_items WHERE pinned != 0))",
        )
        .bind(limit as i64)
        .fetch_all(&self.pool)
        .await
        .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        let mut items = Vec::with_capacity(rows.len());
        for row in rows {
            if let Ok(item) = Self::row_to_item(&row) {
                items.push(item);
            }
        }

        Ok(items)
    }

    async fn touch_last_used_at(&self, id: Uuid) -> Result<()> {
        sqlx::query("UPDATE clipboard_items SET last_used_at = $1 WHERE id = $2")
            .bind(chrono::Utc::now().to_rfc3339())
            .bind(id.to_string())
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        Ok(())
    }

    async fn delete(&self, id: Uuid) -> Result<()> {
        sqlx::query("DELETE FROM clipboard_items WHERE id = $1")
            .bind(id.to_string())
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        Ok(())
    }

    async fn clear(&self) -> Result<()> {
        sqlx::query("DELETE FROM clipboard_items WHERE pinned = 0")
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        Ok(())
    }

    async fn search(&self, query: &str, limit: usize) -> Result<Vec<ClipboardItem>> {
        if query.trim().is_empty() {
            return self.get_all(limit).await;
        }

        self.search_with_fts(query, limit, false).await
    }

    async fn count(&self) -> Result<usize> {
        let row = sqlx::query("SELECT COUNT(*) as count FROM clipboard_items")
            .fetch_one(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        let count: i64 = row
            .try_get("count")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        Ok(count as usize)
    }

    async fn prune(&self, max_items: usize) -> Result<usize> {
        let result = sqlx::query(
            r#"
            DELETE FROM clipboard_items
            WHERE id IN (
                SELECT id FROM clipboard_items
                WHERE pinned = 0
                    ORDER BY last_used_at DESC
                LIMIT -1 OFFSET $1
            )
            "#,
        )
        .bind(max_items as i64)
        .execute(&self.pool)
        .await
        .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        Ok(result.rows_affected() as usize)
    }

    async fn set_pinned(&self, id: Uuid, pinned: bool) -> Result<()> {
        sqlx::query("UPDATE clipboard_items SET pinned_at = CASE WHEN $1 = 0 THEN NULL WHEN pinned = 0 THEN $3 ELSE pinned_at END, pinned = $1 WHERE id = $2")
            .bind(pinned as i32)
            .bind(id.to_string())
            .bind(chrono::Utc::now().to_rfc3339())
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        Ok(())
    }
}

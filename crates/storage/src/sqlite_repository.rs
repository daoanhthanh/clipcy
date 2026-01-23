use std::path::PathBuf;
use std::str::FromStr;

use async_trait::async_trait;
use sqlx::sqlite::{SqliteConnectOptions, SqlitePool, SqlitePoolOptions};
use sqlx::Row;
use uuid::Uuid;

use clipboard_core::domain::{ClipboardItem, ContentType};
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
            .max_connections(5)
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

        let app_dir = data_dir.join("clipcy");
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
                thumbnail BLOB,
                source_app TEXT,
                created_at TEXT NOT NULL,
                pinned INTEGER NOT NULL DEFAULT 0,
                content_hash TEXT
            )
            "#,
        )
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        sqlx::query("CREATE INDEX IF NOT EXISTS idx_created_at ON clipboard_items(created_at DESC)")
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

        Ok(())
    }

    fn row_to_item(row: &sqlx::sqlite::SqliteRow) -> Result<ClipboardItem> {
        let id_str: String = row.try_get("id")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let content_type_str: String = row.try_get("content_type")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let data: Vec<u8> = row.try_get("data")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let preview: String = row.try_get("preview")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let thumbnail: Option<Vec<u8>> = row.try_get("thumbnail")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let source_app: Option<String> = row.try_get("source_app")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let created_at_str: String = row.try_get("created_at")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let pinned: i32 = row.try_get("pinned")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        let content_type: ContentType = serde_json::from_str(&content_type_str)
            .unwrap_or_default();

        let created_at = chrono::DateTime::parse_from_rfc3339(&created_at_str)
            .map(|dt| dt.with_timezone(&chrono::Utc))
            .unwrap_or_default();

        Ok(ClipboardItem {
            id: Uuid::parse_str(&id_str).unwrap_or_default(),
            content_type,
            data,
            preview,
            thumbnail,
            source_app,
            created_at,
            pinned: pinned != 0,
        })
    }

    fn row_to_item_summary(row: &sqlx::sqlite::SqliteRow) -> Result<ClipboardItem> {
        let id_str: String = row.try_get("id")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let content_type_str: String = row.try_get("content_type")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let preview: String = row.try_get("preview")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let thumbnail: Option<Vec<u8>> = row.try_get("thumbnail")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let source_app: Option<String> = row.try_get("source_app")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let created_at_str: String = row.try_get("created_at")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        let pinned: i32 = row.try_get("pinned")
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        let content_type: ContentType = serde_json::from_str(&content_type_str)
            .unwrap_or_default();

        let created_at = chrono::DateTime::parse_from_rfc3339(&created_at_str)
            .map(|dt| dt.with_timezone(&chrono::Utc))
            .unwrap_or_default();

        Ok(ClipboardItem {
            id: Uuid::parse_str(&id_str).unwrap_or_default(),
            content_type,
            data: Vec::new(),
            preview,
            thumbnail,
            source_app,
            created_at,
            pinned: pinned != 0,
        })
    }

    pub async fn get_all_metadata(&self, limit: usize) -> Result<Vec<ClipboardItem>> {
        let rows = sqlx::query(
            "SELECT id, content_type, preview, thumbnail, source_app, created_at, pinned FROM clipboard_items ORDER BY created_at DESC LIMIT $1"
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
        let pattern = format!("%{}%", query);

        let rows = sqlx::query(
            r#"
            SELECT id, content_type, preview, thumbnail, source_app, created_at, pinned
            FROM clipboard_items
            WHERE preview LIKE $1
            ORDER BY created_at DESC
            LIMIT $2
            "#,
        )
            .bind(pattern)
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
}

#[async_trait]
impl ClipboardRepository for SqliteClipboardRepository {
    async fn save(&self, item: &ClipboardItem) -> Result<()> {
        let content_type = serde_json::to_string(&item.content_type)?;
        let content_hash = calculate_content_hash(&item.data);

        // Only deduplicate text content - images should always be kept as separate entries
        if item.content_type == ContentType::Text {
            // Delete any existing item with the same content hash (deduplication)
            // This removes the old entry so the new one will be at the top when sorted by created_at
            sqlx::query("DELETE FROM clipboard_items WHERE content_hash = $1 AND id != $2")
                .bind(&content_hash)
                .bind(item.id.to_string())
                .execute(&self.pool)
                .await
                .map_err(|e| ClipboardError::Storage(e.to_string()))?;
        }

        sqlx::query(
            r#"
            INSERT OR REPLACE INTO clipboard_items
            (id, content_type, data, preview, thumbnail, source_app, created_at, pinned, content_hash)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            "#,
        )
            .bind(item.id.to_string())
            .bind(content_type)
            .bind(&item.data)
            .bind(&item.preview)
            .bind(&item.thumbnail)
            .bind(&item.source_app)
            .bind(item.created_at.to_rfc3339())
            .bind(item.pinned as i32)
            .bind(&content_hash)
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

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
            "SELECT * FROM clipboard_items ORDER BY created_at DESC LIMIT $1"
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
        let pattern = format!("%{}%", query);

        let rows = sqlx::query(
            r#"
            SELECT * FROM clipboard_items
            WHERE preview LIKE $1
            ORDER BY created_at DESC
            LIMIT $2
            "#,
        )
            .bind(pattern)
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

    async fn count(&self) -> Result<usize> {
        let row = sqlx::query("SELECT COUNT(*) as count FROM clipboard_items")
            .fetch_one(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        let count: i64 = row.try_get("count")
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
                ORDER BY created_at DESC
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
        sqlx::query("UPDATE clipboard_items SET pinned = $1 WHERE id = $2")
            .bind(pinned as i32)
            .bind(id.to_string())
            .execute(&self.pool)
            .await
            .map_err(|e| ClipboardError::Storage(e.to_string()))?;

        Ok(())
    }
}

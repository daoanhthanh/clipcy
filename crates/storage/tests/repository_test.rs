use clipboard_core::domain::ClipboardItem;
use clipboard_core::ports::ClipboardRepository;
use clipboard_storage::SqliteClipboardRepository;
use tempfile::tempdir;

#[tokio::test]
async fn test_save_and_retrieve() {
    let dir = tempdir().unwrap();
    let db_path = dir.path().join("test.db");
    let repo = SqliteClipboardRepository::new(db_path).await.unwrap();

    let item = ClipboardItem::new_text("Test content");
    repo.save(&item).await.unwrap();

    let retrieved = repo.get_by_id(item.id).await.unwrap();
    assert!(retrieved.is_some());
    assert_eq!(retrieved.unwrap().as_text(), Some("Test content"));
}

#[tokio::test]
async fn test_search() {
    let dir = tempdir().unwrap();
    let db_path = dir.path().join("test.db");
    let repo = SqliteClipboardRepository::new(db_path).await.unwrap();

    repo.save(&ClipboardItem::new_text("Hello World"))
        .await
        .unwrap();
    repo.save(&ClipboardItem::new_text("Hello Rust"))
        .await
        .unwrap();
    repo.save(&ClipboardItem::new_text("Goodbye"))
        .await
        .unwrap();

    let results = repo.search("Hello", 10).await.unwrap();
    assert_eq!(results.len(), 2);
}

#[tokio::test]
async fn test_search_matches_source_app_and_aliases() {
    let dir = tempdir().unwrap();
    let db_path = dir.path().join("test.db");
    let repo = SqliteClipboardRepository::new(db_path).await.unwrap();

    let content_item = ClipboardItem::new_text("Visual Studio Code");
    repo.save(&content_item).await.unwrap();

    let mut app_item = ClipboardItem::new_text("Other content");
    app_item.source_app = Some("Visual Studio Code".to_string());
    repo.save(&app_item).await.unwrap();

    let results = repo.search("vs code", 10).await.unwrap();

    let ids: Vec<_> = results.iter().map(|item| item.id).collect();
    assert!(ids.contains(&content_item.id));
    assert!(ids.contains(&app_item.id));
}

#[tokio::test]
async fn test_prune() {
    let dir = tempdir().unwrap();
    let db_path = dir.path().join("test.db");
    let repo = SqliteClipboardRepository::new(db_path).await.unwrap();

    for i in 0..10 {
        repo.save(&ClipboardItem::new_text(format!("Item {}", i)))
            .await
            .unwrap();
    }

    let deleted = repo.prune(5).await.unwrap();
    assert_eq!(deleted, 5);

    let count = repo.count().await.unwrap();
    assert_eq!(count, 5);
}

#[tokio::test]
async fn test_get_all_ordered() {
    let dir = tempdir().unwrap();
    let db_path = dir.path().join("test.db");
    let repo = SqliteClipboardRepository::new(db_path).await.unwrap();

    repo.save(&ClipboardItem::new_text("First")).await.unwrap();
    tokio::time::sleep(tokio::time::Duration::from_millis(10)).await;
    repo.save(&ClipboardItem::new_text("Second")).await.unwrap();
    tokio::time::sleep(tokio::time::Duration::from_millis(10)).await;
    repo.save(&ClipboardItem::new_text("Third")).await.unwrap();

    let items = repo.get_all(10).await.unwrap();
    assert_eq!(items.len(), 3);
    assert_eq!(items[0].as_text(), Some("Third"));
    assert_eq!(items[1].as_text(), Some("Second"));
    assert_eq!(items[2].as_text(), Some("First"));
}

#[tokio::test]
async fn test_delete() {
    let dir = tempdir().unwrap();
    let db_path = dir.path().join("test.db");
    let repo = SqliteClipboardRepository::new(db_path).await.unwrap();

    let item = ClipboardItem::new_text("To delete");
    repo.save(&item).await.unwrap();

    repo.delete(item.id).await.unwrap();

    let retrieved = repo.get_by_id(item.id).await.unwrap();
    assert!(retrieved.is_none());
}

#[tokio::test]
async fn test_pinned_items_preserved_on_clear() {
    let dir = tempdir().unwrap();
    let db_path = dir.path().join("test.db");
    let repo = SqliteClipboardRepository::new(db_path).await.unwrap();

    let mut item = ClipboardItem::new_text("Pinned item");
    item.pinned = true;
    repo.save(&item).await.unwrap();

    repo.save(&ClipboardItem::new_text("Not pinned"))
        .await
        .unwrap();

    repo.clear().await.unwrap();

    let count = repo.count().await.unwrap();
    assert_eq!(count, 1);

    let retrieved = repo.get_by_id(item.id).await.unwrap();
    assert!(retrieved.is_some());
}

#[tokio::test]
async fn test_pinned_status_preserved_on_dedup() {
    let dir = tempdir().unwrap();
    let db_path = dir.path().join("test.db");
    let repo = SqliteClipboardRepository::new(db_path).await.unwrap();

    let mut pinned_item = ClipboardItem::new_text("Duplicate");
    pinned_item.pinned = true;
    repo.save(&pinned_item).await.unwrap();

    let duplicate = ClipboardItem::new_text("Duplicate");
    repo.save(&duplicate).await.unwrap();

    let items = repo.get_all(10).await.unwrap();
    assert_eq!(items.len(), 1);
    assert_eq!(items[0].as_text(), Some("Duplicate"));
    assert!(items[0].pinned);
    assert_eq!(items[0].id, pinned_item.id);
    assert!(repo.get_by_id(pinned_item.id).await.unwrap().is_some());
}

#[tokio::test]
async fn test_pin_order_survives_copy_paste_and_restart() {
    let dir = tempdir().unwrap();
    let db_path = dir.path().join("test.db");
    let repo = SqliteClipboardRepository::new(db_path.clone())
        .await
        .unwrap();
    let older = ClipboardItem::new_text("Shared older");
    let newer = ClipboardItem::new_text("Shared newer");
    repo.save(&older).await.unwrap();
    repo.save(&newer).await.unwrap();
    repo.set_pinned(newer.id, true).await.unwrap();
    tokio::time::sleep(tokio::time::Duration::from_millis(10)).await;
    repo.set_pinned(older.id, true).await.unwrap();
    repo.touch_last_used_at(newer.id).await.unwrap();
    repo.save(&ClipboardItem::new_text("Shared newer"))
        .await
        .unwrap();
    repo.set_pinned(newer.id, true).await.unwrap();
    drop(repo);
    let repo = SqliteClipboardRepository::new(db_path.clone())
        .await
        .unwrap();
    for items in [
        repo.get_all(10).await.unwrap(),
        repo.get_all_metadata(10).await.unwrap(),
        repo.search("Shared", 10).await.unwrap(),
        repo.search_metadata("Shared", 10).await.unwrap(),
    ] {
        assert_eq!(
            items.iter().map(|item| item.id).collect::<Vec<_>>(),
            vec![older.id, newer.id]
        );
    }
    repo.set_pinned(newer.id, false).await.unwrap();
    repo.set_pinned(newer.id, true).await.unwrap();
    assert_eq!(repo.get_all(10).await.unwrap()[0].id, newer.id);
}

#[tokio::test]
async fn test_existing_database_backfills_pin_time() {
    let dir = tempdir().unwrap();
    let db_path = dir.path().join("test.db");
    let repo = SqliteClipboardRepository::new(db_path.clone())
        .await
        .unwrap();
    let mut older = ClipboardItem::new_text("Older pin");
    older.pinned = true;
    older.last_used_at = chrono::Utc::now() - chrono::Duration::days(1);
    let mut newer = ClipboardItem::new_text("Newer pin");
    newer.pinned = true;
    repo.save(&older).await.unwrap();
    repo.save(&newer).await.unwrap();
    drop(repo);
    let pool = sqlx::SqlitePool::connect(&format!("sqlite:{}", db_path.display()))
        .await
        .unwrap();
    sqlx::query("ALTER TABLE clipboard_items DROP COLUMN pinned_at")
        .execute(&pool)
        .await
        .unwrap();
    pool.close().await;
    let repo = SqliteClipboardRepository::new(db_path).await.unwrap();
    assert_eq!(repo.get_all(10).await.unwrap()[0].id, newer.id);
    repo.touch_last_used_at(older.id).await.unwrap();
    assert_eq!(repo.get_all_metadata(10).await.unwrap()[0].id, newer.id);
}

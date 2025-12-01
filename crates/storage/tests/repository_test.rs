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

    repo.save(&ClipboardItem::new_text("Hello World")).await.unwrap();
    repo.save(&ClipboardItem::new_text("Hello Rust")).await.unwrap();
    repo.save(&ClipboardItem::new_text("Goodbye")).await.unwrap();

    let results = repo.search("Hello", 10).await.unwrap();
    assert_eq!(results.len(), 2);
}

#[tokio::test]
async fn test_prune() {
    let dir = tempdir().unwrap();
    let db_path = dir.path().join("test.db");
    let repo = SqliteClipboardRepository::new(db_path).await.unwrap();

    for i in 0..10 {
        repo.save(&ClipboardItem::new_text(format!("Item {}", i))).await.unwrap();
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

    repo.save(&ClipboardItem::new_text("Not pinned")).await.unwrap();

    repo.clear().await.unwrap();

    let count = repo.count().await.unwrap();
    assert_eq!(count, 1);

    let retrieved = repo.get_by_id(item.id).await.unwrap();
    assert!(retrieved.is_some());
}

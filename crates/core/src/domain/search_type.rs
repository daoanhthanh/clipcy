/// Search type for filtering clipboard history
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum SearchType {
    /// Fuzzy matching - characters appear in order but not necessarily consecutive
    #[default]
    Fuzzy,
    /// Regular expression matching
    Regex,
}

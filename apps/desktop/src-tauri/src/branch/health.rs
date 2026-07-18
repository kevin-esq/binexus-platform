use super::{validate_branch_url, BranchClient};
use crate::error::AppResult;

pub async fn probe(raw_url: &str) -> AppResult<()> {
    let url = validate_branch_url(raw_url)?;
    BranchClient::new(url)?.health().await.map(|_| ())
}

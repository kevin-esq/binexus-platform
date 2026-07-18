use crate::error::{AppError, AppResult};
use std::net::{IpAddr, ToSocketAddrs};
use url::Url;

pub fn validate_branch_url(raw: &str) -> AppResult<Url> {
    let url = Url::parse(raw).map_err(|_| AppError::UrlPolicy)?;
    if !matches!(url.scheme(), "http" | "https") {
        return Err(AppError::UrlPolicy);
    }
    if !url.username().is_empty() || url.password().is_some() {
        return Err(AppError::UrlPolicy);
    }
    if url.query().is_some() || url.fragment().is_some() {
        return Err(AppError::UrlPolicy);
    }
    let path = url.path();
    if !(path.is_empty() || path == "/") {
        return Err(AppError::UrlPolicy);
    }
    let host = url.host_str().ok_or(AppError::UrlPolicy)?;
    if let Ok(ip) = host.parse::<IpAddr>() {
        if is_allowlisted(ip) {
            return Ok(url);
        }
        return Err(AppError::UrlPolicy);
    }
    let ips = (
        host,
        url.port_or_known_default().ok_or(AppError::UrlPolicy)?,
    )
        .to_socket_addrs()
        .map_err(|_| AppError::UrlPolicy)?
        .map(|value| value.ip())
        .collect::<Vec<_>>();
    validate_resolved_ips(&ips)?;
    Ok(url)
}

pub fn validate_resolved_ips(ips: &[IpAddr]) -> AppResult<()> {
    if ips.is_empty() || ips.iter().any(|ip| !is_allowlisted(*ip)) {
        Err(AppError::UrlPolicy)
    } else {
        Ok(())
    }
}

fn is_allowlisted(ip: IpAddr) -> bool {
    match ip {
        IpAddr::V4(v4) => v4.is_loopback() || v4.is_private(),
        IpAddr::V6(v6) => v6.is_loopback() || v6.is_unique_local(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn allows_loopback_and_private_literals() {
        for value in [
            "http://127.0.0.1:5102",
            "http://10.1.2.3",
            "http://172.16.0.1",
            "http://192.168.1.2",
        ] {
            assert!(validate_branch_url(value).is_ok());
        }
    }
    #[test]
    fn blocks_public_and_metadata_addresses() {
        for value in ["http://8.8.8.8", "http://169.254.169.254"] {
            assert!(validate_branch_url(value).is_err());
        }
    }
    #[test]
    fn rejects_credentials_query_fragment_and_path() {
        for value in [
            "http://user:pass@127.0.0.1:5102",
            "http://127.0.0.1:5102?x=1",
            "http://127.0.0.1:5102#frag",
            "http://127.0.0.1:5102/extra",
        ] {
            assert!(validate_branch_url(value).is_err());
        }
    }
}

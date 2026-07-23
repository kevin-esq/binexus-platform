//! Device Access Token (DAT) lifecycle for Branch operational HTTP.
//!
//! The DAT never crosses IPC, disk, or React. Only public session states are exposed.

mod session;

pub use session::{
    DeviceAuthIdentity, DeviceAuthSession, DeviceSessionPublicState, DEVICE_AUTH_HEADER,
};

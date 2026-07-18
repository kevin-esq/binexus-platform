fn main() {
    tauri_build::build();

    // Windows cargo-test binaries do not receive tauri-build's Common Controls v6
    // manifest (only main bins do via rustc-link-arg-bins). Without it the loader
    // binds System32 comctl32 v5.82, which lacks TaskDialogIndirect / other v6
    // exports statically imported by wry/tao → STATUS_ENTRYPOINT_NOT_FOUND (0xC0000139).
    // See tauri-apps/tauri#13419 / #14580. Duplicate on the app binary is merged harmlessly.
    #[cfg(all(windows, target_env = "msvc"))]
    {
        println!("cargo:rerun-if-changed=windows-comctl-v6.manifest");
        println!(
            "cargo:rustc-link-arg=/MANIFESTDEPENDENCY:type='win32' \
             name='Microsoft.Windows.Common-Controls' \
             version='6.0.0.0' \
             processorArchitecture='*' \
             publicKeyToken='6595b64144ccf1df' \
             language='*'"
        );
    }
}

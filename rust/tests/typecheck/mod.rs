//! Runs the two `typecheck/*` fixtures for real.
//!
//! Rust compiles, so `conformance/skips/rust.json` is empty and these cases have to be
//! executed rather than declared away. Each builds a snippet from `snippets/` in a
//! throwaway crate and asserts `cargo` rejects it.
//!
//! ## Why not `trybuild`
//!
//! `trybuild` is the ecosystem-standard answer and it snapshots rustc's full diagnostic
//! text into a checked-in `.stderr`. That text -- notes, help spans, wording -- changes
//! between rustc releases, and `rust-test.yml` runs a stable + MSRV matrix. A snapshot
//! generated on stable does not match the MSRV compiler's output, and the usual fix
//! (run UI tests on one toolchain only) collides head-on with the completeness assertion:
//! every other matrix cell would then be missing two executed cases.
//!
//! So this asserts on the **error code** instead. `E0599` and `E0308` are stable across
//! releases in a way diagnostic prose is not, so the same assertion holds on every cell.

use libtest_mimic::Failed;
use serde_json::Value;
use std::path::{Path, PathBuf};
use std::process::Command;

/// The rustc error code each snippet must be rejected with. Asserting the code and not
/// merely a non-zero exit is what stops a snippet failing for an unrelated reason -- a
/// typo, a renamed item, a missing import -- from counting as a pass.
const EXPECTATIONS: &[(&str, &str, &str)] = &[
    (
        "typecheck/url-is-not-a-target",
        "url-is-not-a-target.rs",
        // no variant or associated item named `Url`
        "E0599",
    ),
    (
        "typecheck/source-format-not-accepted-as-target",
        "source-format-not-accepted-as-target.rs",
        // mismatched types
        "E0308",
    ),
];

pub fn run(case_id: &str, given: &Value) -> Result<(), Failed> {
    let (_, snippet, expected_code) = EXPECTATIONS
        .iter()
        .find(|(id, _, _)| *id == case_id)
        .ok_or_else(|| {
            format!(
                "fixture {case_id} has no snippet in the Rust runner. Add one to \
                 tests/typecheck/snippets/ rather than skipping the case."
            )
        })?;

    if given["snippet"]
        .as_str()
        .unwrap_or_default()
        .trim()
        .is_empty()
    {
        return Err(format!("fixture {case_id} carries no snippet").into());
    }

    let (stderr, built) = build_snippet(snippet)?;
    if built {
        return Err(format!(
            "{case_id} compiled, but the fixture requires it not to:\n{}",
            given["snippet"]
        )
        .into());
    }
    if !stderr.contains(expected_code) {
        return Err(format!(
            "{case_id} failed to compile, but not with {expected_code} -- so it may be \
             failing for the wrong reason:\n{stderr}"
        )
        .into());
    }

    Ok(())
}

/// The anti-tautology guard. Without it, a harness that reported "did not compile"
/// unconditionally would make both typecheck cases green forever while proving nothing.
pub fn positive_control() -> Result<(), Failed> {
    let (stderr, built) = build_snippet("positive-control.rs")?;
    if !built {
        return Err(format!(
            "the positive control must compile, so the typecheck harness is broken and its \
             two conformance cases prove nothing:\n{stderr}"
        )
        .into());
    }
    Ok(())
}

/// Builds one snippet in a throwaway crate and reports whether it compiled.
fn build_snippet(snippet: &str) -> Result<(String, bool), Failed> {
    let crate_dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    let source = std::fs::read_to_string(crate_dir.join("tests/typecheck/snippets").join(snippet))
        .map_err(|error| format!("could not read the snippet {snippet}: {error}"))?;

    // The package name has to be UNIQUE per snippet. The probes share one target
    // directory (rebuilding the dependency tree per snippet costs real wall-clock), and
    // libtest-mimic runs trials in parallel -- with a shared name, two concurrent builds
    // clobber each other's fingerprint and one picks up the other's artifact. That is not
    // hypothetical: it made both typecheck cases report "compiled" on this suite's first
    // run, which is a false PASS in the direction that matters.
    let package = format!("labelzoom-typecheck-{}", snippet.trim_end_matches(".rs"));
    let probe = std::env::temp_dir().join(&package);
    let _ = std::fs::remove_dir_all(&probe);
    std::fs::create_dir_all(probe.join("src"))?;
    std::fs::write(probe.join("src/main.rs"), source)?;
    std::fs::write(
        probe.join("Cargo.toml"),
        format!(
            // An empty [workspace] table so the probe is not absorbed into a parent
            // workspace, and a path dependency so --offline resolves with no registry.
            //
            // default-features = false drops ureq/rustls/ring: the snippets only name
            // types, so building an HTTP stack to type-check them is pure wall-clock.
            "[workspace]\n\n\
             [package]\nname = \"{package}\"\nversion = \"0.0.0\"\n\
             edition = \"2021\"\n\n\
             [dependencies]\nlabelzoom = {{ path = {crate_dir:?}, default-features = false }}\n"
        ),
    )?;

    let output = Command::new(cargo())
        .args(["build", "--offline", "--quiet"])
        .current_dir(&probe)
        // Share the parent's target directory so serde and ureq are not rebuilt per
        // snippet; without this each probe is a cold build.
        .env("CARGO_TARGET_DIR", target_dir(&crate_dir))
        .output()
        .map_err(|error| format!("could not run cargo for {snippet}: {error}"))?;

    let stderr = String::from_utf8_lossy(&output.stderr).into_owned();
    Ok((stderr, output.status.success()))
}

fn cargo() -> String {
    std::env::var("CARGO").unwrap_or_else(|_| "cargo".to_owned())
}

fn target_dir(crate_dir: &Path) -> PathBuf {
    std::env::var_os("CARGO_TARGET_DIR")
        .map(PathBuf::from)
        .unwrap_or_else(|| crate_dir.join("target"))
}

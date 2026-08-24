package labelzoom_test

import (
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"
)

// The identifier each snippet must be rejected on. Asserting the message and not merely a
// non-zero exit is what stops a snippet that fails for an unrelated reason -- a typo, a
// missing import, a renamed field -- from counting as a pass.
var typecheckExpectations = map[string]string{
	"typecheck/url-is-not-a-target":                  "TargetURL",
	"typecheck/source-format-not-accepted-as-target": "SourcePDF",
}

// snippetFor maps a case id onto its file under testdata/typecheck/.
func snippetFor(caseID string) string {
	_, name, _ := strings.Cut(caseID, "/")
	return name + ".go.txt"
}

// runTypecheckCase compiles the snippet the fixture describes and asserts it does NOT build.
//
// Go compiles, so conformance/skips/go.json is empty and these two cases have to be run for
// real rather than declared away. Compiling is also stronger than the reflective stand-in
// the Java and .NET suites use: asserting "TargetFormat has no URL member" still passes if
// someone widens ConvertRequest.To to a plain string, which is exactly the regression these
// fixtures exist to catch.
func runTypecheckCase(t *testing.T, caseID string, g given) {
	t.Helper()

	wantIdentifier, known := typecheckExpectations[caseID]
	if !known {
		t.Fatalf("fixture %s has no snippet in the Go runner. Add one to "+
			"testdata/typecheck/ rather than skipping the case.", caseID)
	}
	if strings.TrimSpace(g.Snippet) == "" {
		t.Fatalf("fixture %s carries no snippet", caseID)
	}

	stderr, built := buildSnippet(t, snippetFor(caseID))
	if built {
		t.Fatalf("%s compiled, but the fixture requires it not to:\n%s", caseID, g.Snippet)
	}
	if !strings.Contains(stderr, wantIdentifier) {
		t.Errorf("%s failed to compile, but not on %s -- so it may be failing for the wrong "+
			"reason:\n%s", caseID, wantIdentifier, stderr)
	}
}

// TestTypecheckHarnessRejectsOnlyWhatItShould is the anti-tautology guard.
//
// Without it, a harness that reported "did not compile" unconditionally -- a bad temp
// module, an unset GOFLAGS, a missing replace directive -- would make both typecheck cases
// green forever while proving nothing.
func TestTypecheckHarnessRejectsOnlyWhatItShould(t *testing.T) {
	stderr, built := buildSnippet(t, "positive-control.go.txt")
	if !built {
		t.Fatalf("the positive control must compile, so the typecheck harness is broken "+
			"and its two conformance cases prove nothing:\n%s", stderr)
	}
}

// buildSnippet compiles one testdata snippet in a throwaway module and reports whether it
// built, along with the compiler's output.
func buildSnippet(t *testing.T, name string) (stderr string, built bool) {
	t.Helper()

	sdk, err := filepath.Abs(".")
	if err != nil {
		t.Fatalf("could not resolve the module directory: %v", err)
	}

	source, err := os.ReadFile(filepath.Join("testdata", "typecheck", name)) //nolint:gosec // fixed testdata path
	if err != nil {
		t.Fatalf("could not read the snippet %s: %v", name, err)
	}

	dir := t.TempDir()
	write := func(file, content string) {
		if err := os.WriteFile(filepath.Join(dir, file), []byte(content), 0o600); err != nil {
			t.Fatalf("could not write %s: %v", file, err)
		}
	}
	write("main.go", string(source))
	// A replace directive against the local module, and the SDK has no dependencies of its
	// own, so this resolves with GOPROXY=off -- the probe never touches the network.
	write("go.mod", "module labelzoomtypecheckprobe\n\ngo 1.23\n\n"+
		"require github.com/labelzoom/labelzoom-sdk/go v0.0.0\n\n"+
		"replace github.com/labelzoom/labelzoom-sdk/go => "+sdk+"\n")

	command := exec.Command(goTool(t), "build", "-o", os.DevNull, ".") //nolint:gosec // fixed argv, temp dir
	command.Dir = dir
	command.Env = append(os.Environ(),
		"GOFLAGS=-mod=mod",
		// Neither the module cache nor a parent go.work may influence the result.
		"GOPROXY=off",
		"GOWORK=off",
	)
	output, err := command.CombinedOutput()
	return string(output), err == nil
}

func goTool(t *testing.T) string {
	t.Helper()
	if tool, err := exec.LookPath("go"); err == nil {
		return tool
	}
	if root := os.Getenv("GOROOT"); root != "" {
		return filepath.Join(root, "bin", "go")
	}
	t.Skip("the go tool is not on PATH, so the typecheck snippets cannot be compiled")
	return ""
}

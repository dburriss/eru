module Eru.Tests.PatternsTests

open Xunit
open Eru

// ── isBinaryContent ──────────────────────────────────────────────────────────

[<Fact>]
let ``isBinaryContent returns false for plain text`` () =
    Assert.False(Patterns.isBinaryContent "hello world\nsome text")

[<Fact>]
let ``isBinaryContent returns true when content contains null byte`` () =
    Assert.True(Patterns.isBinaryContent "some\x00binary")

[<Fact>]
let ``isBinaryContent returns false for empty string`` () =
    Assert.False(Patterns.isBinaryContent "")

// ── matchesGlob ──────────────────────────────────────────────────────────────

[<Fact>]
let ``matchesGlob exact filename match`` () =
    Assert.True(Patterns.matchesGlob "README.md" "README.md")

[<Fact>]
let ``matchesGlob exact match is case insensitive`` () =
    Assert.True(Patterns.matchesGlob "readme.md" "README.md")

[<Fact>]
let ``matchesGlob star extension matches filename`` () =
    Assert.True(Patterns.matchesGlob "*.exe" "setup.exe")

[<Fact>]
let ``matchesGlob star extension matches filename in subdirectory`` () =
    Assert.True(Patterns.matchesGlob "*.exe" "bin/setup.exe")

[<Fact>]
let ``matchesGlob star extension does not match different extension`` () =
    Assert.False(Patterns.matchesGlob "*.exe" "notes.md")

[<Fact>]
let ``matchesGlob star does not cross directory separator`` () =
    Assert.False(Patterns.matchesGlob "docs/*.md" "docs/sub/file.md")

[<Fact>]
let ``matchesGlob double star crosses directory separator`` () =
    Assert.True(Patterns.matchesGlob "docs/**/*.md" "docs/sub/file.md")

[<Fact>]
let ``matchesGlob double star matches immediate child`` () =
    Assert.True(Patterns.matchesGlob "docs/**/*.md" "docs/file.md")

[<Fact>]
let ``matchesGlob question mark matches single char`` () =
    Assert.True(Patterns.matchesGlob "file?.md" "file1.md")

[<Fact>]
let ``matchesGlob question mark does not match slash`` () =
    Assert.False(Patterns.matchesGlob "file?.md" "file/.md")

[<Fact>]
let ``matchesGlob path-scoped pattern matches full path`` () =
    Assert.True(Patterns.matchesGlob "scripts/*.sh" "scripts/deploy.sh")

[<Fact>]
let ``matchesGlob path-scoped pattern does not match different directory`` () =
    Assert.False(Patterns.matchesGlob "scripts/*.sh" "other/deploy.sh")

// ── isPathBlocked ────────────────────────────────────────────────────────────

[<Fact>]
let ``isPathBlocked returns true when path matches block pattern`` () =
    Assert.True(Patterns.isPathBlocked ["*.exe"] [] "setup.exe")

[<Fact>]
let ``isPathBlocked returns false when allow pattern overrides block`` () =
    Assert.False(Patterns.isPathBlocked ["*.exe"] ["setup.exe"] "setup.exe")

[<Fact>]
let ``isPathBlocked returns false when no patterns match`` () =
    Assert.False(Patterns.isPathBlocked ["*.exe"] [] "README.md")

// ── isBlocked ────────────────────────────────────────────────────────────────

[<Fact>]
let ``isBlocked blocks file matching extension pattern`` () =
    Assert.True(Patterns.isBlocked ["*.exe"] [] false "setup.exe" "text content")

[<Fact>]
let ``isBlocked allow pattern overrides block pattern`` () =
    Assert.False(Patterns.isBlocked ["*.exe"] ["setup.exe"] false "setup.exe" "text content")

[<Fact>]
let ``isBlocked blocks binary content when allowBinaries is false`` () =
    Assert.True(Patterns.isBlocked [] [] false "myapp" "binary\x00content")

[<Fact>]
let ``isBlocked allows binary content when allowBinaries is true`` () =
    Assert.False(Patterns.isBlocked [] [] true "myapp" "binary\x00content")

[<Fact>]
let ``isBlocked allow pattern wins over binary content check`` () =
    Assert.False(Patterns.isBlocked ["*"] ["myapp"] false "myapp" "binary\x00content")

[<Fact>]
let ``isBlocked returns false for plain text file with no matching patterns`` () =
    Assert.False(Patterns.isBlocked ["*.exe"] [] false "README.md" "# Hello")

[<Fact>]
let ``isBlocked Makefile passes when blockPatterns contains no-ext sentinel and allowPatterns lists Makefile`` () =
    Assert.False(Patterns.isBlocked [] ["Makefile"] false "Makefile" "build: all")

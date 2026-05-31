---
description: Cheat sheet for mise, a polyglot tool version manager for runtimes, env vars, and tasks.
tags: [dev-tools, ai]
---

# Mise Cheat Sheet

> Polyglot tool version manager (replaces asdf, nvm, pyenv, rbenv, tfenv). Single Rust binary.  
> Manages runtimes, environment variables, and tasks per project.

## Installation

```bash
curl https://mise.run | sh
brew install mise          # macOS
sudo apt install -y mise   # Debian/Ubuntu
dnf install -y mise        # RHEL/Fedora
```

**Shell activation (required for auto-switching):**
```bash
eval "$(mise activate bash)"    # ~/.bashrc
eval "$(mise activate zsh)"     # ~/.zshrc
mise activate fish | source     # ~/.config/fish/config.fish
```

## Tool Management

```bash
mise install node@latest                    # Latest version
mise install node@20.11.0                   # Specific version
mise install                                # All from config
mise use node@20                            # Set for directory
mise use --global node@20                   # Global default
mise shell node@18                          # Current session only
mise exec node@18 -- node --version         # One-off execution
mise uninstall node@18.19.0                 # Remove version
mise prune                                  # Remove unused versions
```

## Check Versions

```bash
mise current                 # Active versions
mise ls                      # All installed
mise ls node                 # One tool
mise ls-remote node          # Available remote versions
mise which node              # Binary location
mise doctor                  # Installation health
```

## Configuration Files

### .mise.toml (Project Root)

```toml
[tools]
node = "20"                    # Latest 20.x.x
python = "3.12.2"             # Exact version
go = "latest"                 # Latest stable
node = ["20", "18"]           # Multiple versions

[env]
DATABASE_URL = "postgres://localhost:5432/myapp"
NODE_ENV = "development"
_.file = [".env", ".env.local"]
_.path = ["./bin", "./node_modules/.bin"]

[tasks.build]
run = "npm run build"
description = "Build the application"
```

### .tool-versions (asdf Compatibility)

```
node 20.11.0
python 3.12.2
terraform 1.8.0
```

## Configuration Priority

1. `MISE_<TOOL>_VERSION` environment variable (highest)
2. `.mise.toml` in current directory
3. `.tool-versions` in current directory
4. `.mise.toml` in parent directories
5. `.tool-versions` in parent directories
6. `~/.config/mise/config.toml` (lowest/global)

Also reads: `.node-version`, `.python-version`, `.ruby-version`

## Task Runner

```bash
mise run build               # Run task
mise r build                 # Shorthand
mise run greet World         # With arguments
mise tasks                   # List tasks
mise watch -t test           # Watch mode
```

**File-based tasks** — create executable scripts in `mise/tasks/` with metadata comments:
```bash
#!/bin/bash
# mise description="Backup the database"
# mise depends=["check-env"]
```

## Trust Management

```bash
mise trust                   # Trust current directory
mise trust .mise.toml        # Trust specific file
mise trust --untrust         # Revoke trust
mise trust ls                # List trusted paths
```

Tool versions work without trust; env vars and tasks require it.

## Backends

```bash
mise use node@20                           # Core (built-in)
mise use terraform@1.8                     # asdf plugins
mise use ubi:junegunn/fzf                  # GitHub releases
mise use cargo:ripgrep                     # Rust crates
mise use npm:prettier                      # Global npm tools
mise use pipx:ansible-lint                 # Python CLI tools
mise use go:golang.org/x/tools/gopls       # Go modules
```

## Settings

```bash
mise settings                              # List all
mise settings get auto_install             # Get setting
mise settings set auto_install true        # Set setting
```

**~/.config/mise/settings.toml:**
```toml
auto_install = true
jobs = 4
verbose = false
trusted_config_paths = ["/home/deploy/projects"]
```

## CI/CD Integration

**GitHub Actions:**
```yaml
- uses: jdx/mise-action@v2
- run: mise run test
```

**GitLab CI / Generic:**
```bash
curl https://mise.run | sh
eval "$(~/.local/bin/mise activate bash)"
mise install
mise run test
```

**Docker:**
```dockerfile
RUN curl https://mise.run | sh
ENV PATH="/root/.local/bin:$PATH"
COPY .mise.toml .
RUN mise install
```

## Self-Management

```bash
mise self-update              # Update mise
mise outdated                 # List outdated tools
mise upgrade                  # Upgrade to latest in-range
mise cache clear              # Clear cache
rm -rf ~/.local/share/mise ~/.config/mise ~/.local/bin/mise  # Uninstall
```

## Common Patterns

**New project setup:**
```bash
cd ~/new-project
mise use node@20 python@3.12
mise trust
git add .mise.toml && git commit -m "Add mise config"
```

**Onboarding an existing project:**
```bash
git clone git@github.com:team/project.git && cd project
mise trust && mise install
```

**Temporary version:**
```bash
mise exec python@3.11 -- python script.py
```

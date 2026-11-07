# Sample site for themes and screenshots

A small, stable eru project to generate a site from when working on site themes, CSS or docs screenshots. It holds
five made-up markdown files in `source/` and a config with `site.showSyncStatus: true`. Edit the files or the config
freely; keep the content fictional, and never copy real internal documents here.

`run.sh` runs `eru` in this folder with an empty throwaway config and cache directory (`XDG_CONFIG_HOME` and
`XDG_CACHE_HOME`), so your own global sources never appear in the output. Set `ERU` if `eru` is not on the PATH.

```bash
cd samples/theme-site
./run.sh sync                                       # index the sample source (first run, or after editing source/)
ERU__SITE__THEME=cyberpunk ./run.sh site generate -o /tmp/theme-site
```

For screenshots, follow "Regenerating the screenshots" in
[docs/reference/theme-list.md](../../docs/reference/theme-list.md#regenerating-the-screenshots) with this site.

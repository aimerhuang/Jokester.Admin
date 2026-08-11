# Prompt lexicon preparation tool

This offline tool prepares third-party word lists for human review. It does not
download sources, connect to MySQL, or enable any rule.

## Input manifest

Copy `manifest.example.json` outside the repository or into an ignored working
directory. One manifest must represent exactly one upstream source and license;
do not mix locally curated files with a third-party manifest. Record the HTTPS
source URL, immutable 40/64-character commit SHA, supported SPDX license
identifier, and expected SHA-256 for every input. Input paths are resolved
relative to the manifest and preparation stops if a hash differs.

Supported formats:

- `lines`: one term per line.
- `comma`: comma, Chinese comma, or line separated terms.
- `tagged`: one term followed by whitespace and comma-separated numeric tags,
  as used by `houbb/sensitive-word-data`.

For tagged files, only tags present in `tagMappings` are retained. `tagPriority`
selects one project category when a source term has several tags. The example
intentionally omits broad political and gambling tags; importing those as image
safety blocks would create substantial false positives.

`houbb/sensitive-word-data` only defines `0=politics`, `1=drugs`,
`2=sexual content`, `3=gambling`, and `4=illegal activity`. Those five source
tags cannot be mechanically split into the project's seven image-safety
categories by `tagMappings`. Use the tagged output only as a disabled review
queue, then classify each retained row manually. Keep project-maintained gap
terms in a separate manifest or reviewed migration so their provenance is not
attributed to houbb.

## Run

```powershell
dotnet run --project .\tools\Jokester.PromptLexiconTool -- prepare `
  --manifest D:\lexicon-review\manifest.json `
  --output D:\lexicon-review\candidates.csv `
  --report D:\lexicon-review\report.json
```

The tool uses the application's `AiPromptTextNormalizer` and term-key algorithm.
It produces:

- a deterministic CSV sorted by category and normalized term;
- `status=0` for every candidate;
- `short_term`, `url_like`, and `category_conflict` review flags;
- `spreadsheet_formula` flags and formula-neutralized review cells;
- source-file SHA-256 values and aggregate counts in the JSON report.

Output and report paths must not already exist. The tool never overwrites them;
use new paths for each reviewed run. Duplicate normalized terms with conflicting
language or proposed action stop preparation because the database term key cannot
represent both behaviors safely.

Do not convert candidates to `status=1` in bulk. Remove irrelevant website,
advertising, political, person-name, and short-word entries; map remaining terms
to the image-safety taxonomy; then run normal and adversarial prompt regression
sets before producing a reviewed database migration.

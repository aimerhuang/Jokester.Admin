# Third-Party Notices

## houbb/sensitive-word-data

The curated, disabled candidate terms in
`docs/migrations/20260811-expand-ai-prompt-sensitive-words-houbb.sql` include
selected data from `houbb/sensitive-word-data`:

- Source: https://github.com/houbb/sensitive-word-data
- Commit: `fe6fc2921836217b8c90619db81b24af8b22d80f`
- Source file: `src/main/resources/sensitive_word_tags.txt`
- Upstream Git blob SHA-256: `37cea2687a1525a436aaa080e918f6c263310bd21b4bce8b05ba5185ee3e5ae8`
- Reviewed CRLF working-copy SHA-256: `d2ca6f91477238577743e8cfebee71e448b32d2477959c2aa7ba49482b3bd142`
- License: Apache License 2.0; see [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt)

The project selected a small subset, preserved the original numeric tags as
provenance, and manually reclassified the terms into its own image-safety
taxonomy. The upstream repository does not provide these seven categories.
Project-maintained supplemental rules are identified separately with
`source_code=project-curated` and are not attributed to houbb.

## YouMind awesome-gpt-image-2

The prompt-library synchronization feature reads the Simplified Chinese
snapshot from `YouMind-OpenLab/awesome-gpt-image-2`:

- Source: https://github.com/YouMind-OpenLab/awesome-gpt-image-2
- Commit: `589f148fd605574569580665403311c5eb88143e`
- Source file: `README_zh.md`
- License/attribution: upstream content is presented under CC BY 4.0; the
  application must retain visible YouMind attribution.

The configured snapshot is parsed into 126 Chinese prompt entries, and the
first upstream image for each entry may be cached on the application server.
CC BY 4.0 attribution does not remove third-party trademark, publicity,
portrait, or privacy rights. Deployment owners must review those rights before
commercial display or redistribution of cached example images.

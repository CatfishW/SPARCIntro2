# Classroom Animation Source Log

Date checked: 2026-03-19

## Scope

This log tracks external animation/asset licensing references for classroom NPC animation sourcing.
It is an implementation aid, not legal advice.

## Sources Checked

- Adobe Mixamo FAQ: https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html
- Adobe Mixamo community licensing clarification: https://community.adobe.com/t5/mixamo-discussions/mixamo-faq-licensing-royalties-ownership-eula-and-tos/m-p/13234775
- Mixamo Additional Terms PDF (effective June 23, 2021): https://wwwimages2.adobe.com/content/dam/cc/en/legal/servicetou/Mixamo-Addl-Terms-en_US-20210623.pdf
- Kenney Mini Arena page: https://kenney.nl/assets/mini-arena
- CC0 1.0 summary: https://creativecommons.org/publicdomain/zero/1.0/

## Practical Use Rules (for this project)

### Mixamo

- Allowed use baseline: Mixamo FAQ states characters/animations are royalty-free for personal, commercial, and non-profit project usage (games/films/etc).
- Distribution constraint: Adobe community clarification states raw character/animation files should not be redistributed as the core product (for example, asset packs/templates with raw files).
- Team sharing note: Team members on the same project can access downloaded files, but distribution to customers/non-team members as raw files is not allowed.
- AI/ML restriction: Mixamo Additional Terms include a specific restriction against using Mixamo services/content/output to create/train/test/improve ML or AI systems.

Project policy derived from this:
- Import Mixamo animations only as integrated game content.
- Do not ship a standalone raw-animation pack from this repository.
- Keep attribution optional (as indicated by Adobe clarification), but preserve source records in docs.
- Do not use Mixamo-derived assets for model training workflows.

### Kenney / CC0

- Kenney Mini Arena lists license as Creative Commons CC0.
- CC0 summary allows copying/modifying/distributing/commercial use without permission.
- CC0 caveats still apply (for example, trademark/publicity/privacy and no implied endorsement).

Project policy derived from this:
- Kenney CC0 assets are approved for direct integration and modification.
- Preserve source metadata in docs for provenance even where attribution is not required.

## Current Classroom Upgrade Usage Notes

- Immediate NPC action support currently relies on local animation/controller logic already in project runtime.
- External source import remains optional and should follow the above constraints before shipping.

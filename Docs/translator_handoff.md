# Byzantium1071 Translation Handoff

## Files to translate
- `_Module/ModuleData/Languages/Chinese/std_module_strings_xml.xml`

## Reference files (do not ship)
- `_Module/ModuleData/Languages/std_module_strings_xml.xml` (canonical English source)
- `Docs/localization_key_defaults.tsv` (key + English default text)

## Rules
1. Keep every `id` exactly unchanged.
2. Translate only each `text` value.
3. Do **not** change placeholders like `{COUNT}`, `{TOWN}`, `{MAXTIER}`.
4. Do **not** change Bannerlord plural/markup tokens like `{@Plural}` and `{\@}`.
5. Keep escaped apostrophes/entities valid XML (e.g., `&apos;`, `&amp;`).
6. Keep line breaks (`\n`) where present in the source text.
7. Emoji/symbols are optional, but preserving them is preferred for UI consistency.

## Quick validation before return
- XML opens without parse errors.
- Number of `<string>` entries remains the same.
- No missing keys, no duplicate keys.
- Placeholders in translated text match the English source for each key.

## Packaging to return
- Return the updated `std_module_strings_xml.xml` only.

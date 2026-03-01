# Byzantium1071 In-Game Smoke Checklist (3 minutes)

## Goal
Quickly verify that localization work did not break gameplay/UI behavior.

## Setup (30s)
- Enable `Byzantium1071` and dependencies.
- Load any campaign save where you can access:
  - at least one town,
  - at least one castle,
  - MCM settings menu.

## Step 1 — MCM screen renders (30s)
- Open MCM for Campaign++.
- Expand 2-3 sections and hover at least 3 settings.
- PASS if labels and hint text display normally (no raw key text like `{=...}`, no blank rows).

## Step 2 — Overlay/UI labels render (20s)
- On campaign map, toggle the overlay with your configured hotkey.
- Switch 3 tabs (for example: Current, Towns, Villages).
- PASS if tab labels and title text render correctly and no missing-text placeholders appear.

## Step 3 — Town interaction text sanity (40s)
- Enter a town menu where slave/town investment actions are available.
- Open at least one related tooltip/message path.
- PASS if text is readable, variables are substituted (numbers/names show), and no placeholder tokens remain visible.

## Step 4 — Castle recruitment screen sanity (40s)
- Enter castle interaction and open the recruitment/deposit flow.
- Check section headers, buttons, and one tooltip/hint.
- PASS if screen opens without errors and all labels/messages appear correctly.

## Step 5 — One gameplay action still works (20s)
- Perform one safe action (e.g., open/close recruitment UI, trigger one investment prompt, or complete one eligible menu action).
- PASS if behavior executes normally (no crash, no stuck menu, no missing callback behavior).

## Pass Criteria
- All 5 steps PASS.
- No crashes, no broken menus, no visible raw localization keys in normal UI text.

## If any step fails
Capture:
1. exact step number,
2. location/menu,
3. visible text or error,
4. screenshot (if possible).

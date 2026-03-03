# Byzantium1071 In-Game Smoke Checklist (~5 minutes)

## Goal
Verify that core systems, new v0.2.5.5/v0.2.6.0 features, and localization all work correctly in-game.

## Setup (30s)
- Enable `Byzantium1071` and dependencies.
- Load any campaign save where you can access:
  - at least one town,
  - at least one castle,
  - MCM settings menu.

## Step 1 — Session-launch compatibility popup (20s)  ← NEW v0.2.5.5
- Load (or start) a campaign.
- PASS if the "Campaign++ - Playing Well With Others?" popup appears automatically.
- PASS if it has two buttons: **OK** and **Copy Report**.
- PASS if clicking **Copy Report** pastes a readable report to clipboard without crash.
- PASS if clicking **OK** dismisses it cleanly.
- Note: if you have "Suppress compat popup" enabled in MCM the popup won't show — disable it to test.

## Step 2 — MCM: all three Campaign++ tabs render (40s)  ← NEW v0.2.5.5/v0.2.6.0
- Open MCM for Campaign++.
- Confirm three tabs exist: **Campaign++**, **Campaign++ - Quick Settings**, **Campaign++ Compatibility**.
- In **Campaign++**: expand 2-3 sections, hover at least 3 settings.
  - PASS if labels and hint text show normally (no raw `{=...}` tokens, no blank rows).
- In **Campaign++ - Quick Settings**: confirm 5 groups render (Core Systems, Economy & Investment, Recruitment & Military, Province & Governance, Immersion & Modifiers).
  - PASS if all group names and toggle labels display, all toggles are clickable.
- In **Campaign++ Compatibility**: confirm the "Playing Well With Others?" button is present.
  - Click the button. PASS if the popup appears (same as Step 1).
  - Confirm the "Suppress compat popup" toggle is present and toggleable.

## Step 3 — Overlay/UI labels render (20s)
- On campaign map, toggle the overlay with your configured hotkey.
- Switch 3 tabs (for example: Current, Towns, Villages).
- PASS if tab labels and title text render correctly and no missing-text placeholders appear.

## Step 4 — Town interaction text sanity (40s)
- Enter a town menu where slave/town investment actions are available.
- Check the **Enslave prisoners** menu entry label and tooltip.
- Check the **Invest in town** menu entry label (should show cost in denars, e.g. `need 2000d` — not a coin glyph ⬮).
- If an investment is active, hover the entry and confirm `(active - X days remaining)` shows cleanly.
- PASS if text is readable, variables are substituted (numbers/names show), and no placeholder tokens remain visible.

## Step 5 — Village investment text sanity (20s)
- Enter a village interaction menu.
- Check the **Invest in village** label and tooltip (same `need Xd` and `active - X days` checks as Step 4).
- PASS if text renders without raw keys or broken glyphs.

## Step 6 — Castle recruitment screen sanity (40s)
- Enter castle interaction and open the recruitment/deposit flow.
- Check the manpower-block tooltip if troops are available (should read `Manpower: cannot recruit ... needs X, only Y left`).
- Check section headers (Pending, Ready to Recruit) and one recruit button tooltip.
- PASS if screen opens without errors and all labels/messages appear correctly.

## Step 7 — One gameplay action still works (20s)
- Perform one safe action (e.g., open/close recruitment UI, trigger one investment prompt, or complete one eligible menu action).
- PASS if behavior executes normally (no crash, no stuck menu, no missing callback behavior).

## Pass Criteria
- All 7 steps PASS.
- No crashes, no broken menus, no visible raw localization keys (`{=...}`) in normal UI text.
- No broken/missing glyphs in investment menu labels (denars shown as `d`, not as ⬮ or □).

## If any step fails
Capture:
1. exact step number,
2. location/menu,
3. visible text or error,
4. screenshot (if possible).

r"""Phase 1 spike dump analyzer (kept for HS/HDT patch investigations, docs/maintenance.md).

Usage:
  1. set "spike_dump": true in %APPDATA%\HearthstoneDeckTracker\HsbgKansousen\config.json, restart HDT, play 1 game
  2. python scripts/analyze_spike_dump.py "<output_dir>\.spike\<game folder>"
     (output_dir defaults to %USERPROFILE%\Documents\HSBG-Logs)

Prints per-file state, hero pick, heroes/placements, per-turn shop/combat data, damage events, HP timeline,
trinkets and undefined tags. Written 2026-09-25 against HDT 1.58.3 dumps.
"""

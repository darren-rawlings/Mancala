# Mancala: design

Working store title: **Pit Stop: Mancala**. Package `com.renegadegames.mancala` (neutral, so the
title can change before release without touching it).

## The game

The classic two-row count-and-capture game (the "Kalah" rules most people know):

- Two rows of six **pits**, one row each, with a big **store** at each end; yours is on your right.
- Each pit starts with **4 stones** (48 in all).
- On your turn, pick up **all** the stones from one of your pits and **sow** them one at a time
  into the following pits, counter-clockwise, including your own store but **skipping your
  opponent's store**.
- **Last stone in your store**: you go again.
- **Last stone in an empty pit on your side**, and the pit opposite has stones: you **capture**
  that last stone and everything opposite, straight into your store.
- When either player's six pits are all empty, the game ends; each player banks the stones left
  on their own side. **Most stones in the store wins.**

## Feel

Counter Fire's lesson: real-feeling pieces, honest sounds, one unforgettable effect.

- A carved **wooden board** with deep round pits. The stones are **glass gems** in mixed colours
  (the traditional look), real physics bodies that tumble and settle into little piles.
- **Sowing** is visible: the stones lift out of the pit together, then drop one at a time into
  each pit along the way, each landing with a glassy **clink** (the glass sound we tuned for
  phone speakers).
- **Signature effect, "the pit stop"**: on a capture, the captured gems flare with light and
  stream into your store in a glowing arc, like a pit crew grabbing everything at once. Landing in
  your own store for an extra turn makes the store pulse.
- **Counts** float above each pit and store, so nobody has to count gems by eye.

## Modes

- Player vs player on one phone.
- Player vs computer: **Easy** (mostly random, takes obvious extra turns), **Medium** (looks a few
  moves ahead), **Hard** (alpha-beta search, deep enough to be properly tough on a phone).

## Ads

Same as Counter Fire, from renegade-core:
- Between-games interstitial every 5th finished game.
- **Undo (ad)** vs the computer: a rewarded ad takes back your last turn (and the computer's reply).

## Scope for version 1

- Kalah rules, 4 stones per pit; PvP and PvC with three levels; undo vs computer.
- Glass gems on a wooden board; sowing, capture and extra-turn effects; counts; sounds.
- Settings: sound on/off, mode, difficulty.

## Later

- Board and gem styles (stone, shell, marble), as Counter Fire has.
- Rule options: 3/5/6 stones per pit; the "empty capture" variant.
- Other mancala families (Oware, Bao).

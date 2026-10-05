# Startup branding

Artwork, downloaded 2026-10-05:

- Double T: https://www.ttu.edu/brand/images/visual-identity/double-t.svg
- Whitacre college signature (established serif design, mirrored from the official
  TTU identity site): https://commons.wikimedia.org/wiki/File:Whitacre_College_of_Engineering_logo.svg
- Original identity source listed by that mirror:
  http://www.depts.ttu.edu/communications/identityguidelines/idguidelines/ttu/official/double-t.php
- Branding reference: https://www.ttu.edu/brand/visual-identity/

PNGs are transparent raster exports of the SVGs; the signature's outer
transparent padding is trimmed. Colors, lettering and proportions are preserved.
The college signature includes the Double T and complete official college name.
The current university download library requires university SharePoint sign-in;
the public formal-signature SVG is a placeholder containing "COLLEGE NAME" and
is not used here. Replace the PNG with a newer approved Whitacre export when available.

`IntroSettings.json` controls durations in seconds. The entry scene preloads
`ELTSDesktop`, holds the final wordmark, then fades into the participant scene.
It uses real elapsed time and never modifies the experiment clock or recordings.
The default Unity splash is disabled in Player Settings. To regenerate the entry
scene, use **ELTS > Create Texas Tech startup intro** in the pinned Unity editor.

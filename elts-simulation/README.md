# ELTS testing environment visualization

An offline, low-detail 3D spatial demonstration based on the ELTS project briefing, particularly sections 2, 5, 6.2, and 6.6. It is a concept visualization, not the Unity acquisition application or a validated apparatus model.

## Use

Open `index.html` in a modern browser. No installation, server, internet connection, or hardware is needed. Copy the entire directory to another computer to move the project.

Drag the scene to orbit, scroll to zoom, or select a keyboard-accessible camera preset. Pause the synthetic motion to inspect geometry. Change the four study conditions, participant position, display width, and illustrative aim offset. The small participant display recomputes projection from the eye through the physical screen as the head moves.

Use **Explore equipment** to read each component's role and locate it with a dashed white ring. **Reset scene** restores the configured camera, study condition, sliders, visibility controls, and simulation time. Playback after reset respects the browser's reduced-motion preference.

## What comes from the briefing

- Conventional display with a world-registered virtual scene behind it; no headset.
- One base station above the display; a forehead tracker and a mock-weapon tracker.
- Three ELTS turrets: two outer LED units and a central Brio camera.
- Four WE/NE × MT/FT conditions.
- Eye-based view frustum, bore/screen intersection, and world-space angular aim error.

## Illustrative choices and limits

Room dimensions, display dimensions, assembly placement, target positions and sizes, movement paths, and participant proportions are demonstration values. They are not measurements or approved protocol settings. The physical display is translucent in the operator view to expose the virtual space behind it; the inset depicts an opaque participant display.

The synthetic mock weapon follows the central reference target with a user-adjustable horizontal angular offset. The readout is the actual 3D angle to that reference target, not necessarily to the nearest target. No effect of ELTS on aiming accuracy is assumed. MT uses smooth deterministic demonstration paths, not the as-yet-unspecified randomized study target behavior. Light paths are symbolic lines, not optical or eye-exposure calculations. Their placement does not implement or validate the independent Brio/Jetson vision pipeline. Base-station direction is symbolic, not a measured coverage volume. Eye markers are positional references, not gaze measurements.

There is no hardware connection, empirical tracking data, CSV replay, shot scoring, or study session execution. Tracking gizmos show synthetic positions; dropout behavior is not simulated. Auxiliary physiological and eye-tracking instruments are omitted because their placement and models are unspecified.

## Modify

`elts-environment.html` is the editable source. The `CONFIG` object at the beginning of its script groups geometry, colors, and motion defaults. The scene uses x = sideways, y = height, z = distance in front of the screen; the physical screen is at z = 0 and virtual targets have negative z.

The code separates vector/projection helpers, drawing primitives, time-based scenario generation, participant projection, scene composition, and controls. It uses a small canvas-based perspective renderer with depth-sorted faces, so it works offline without a GPU library. Overlapping transparent geometry can show sorting artifacts at unusual viewing angles.

`build.mjs` wraps the fragment with a minimal standalone theme to create `index.html`. After editing, run `node build.mjs` in this directory (Node.js is needed only to rebuild, not to view). All paths are relative to the builder's location.

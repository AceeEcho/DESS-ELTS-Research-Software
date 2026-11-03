# Libraries and API references

- **Three.js 0.180.0**: `web/vendor/build/three.module.js`, `three.core.js`, and
  `web/vendor/examples/jsm/controls/OrbitControls.js`. MIT license retained in
  `web/vendor/LICENSE`. Distributed from the versioned npm `three` package.
  [Source](https://github.com/mrdoob/three.js/tree/r180),
  [OrbitControls](https://threejs.org/docs/pages/OrbitControls.html),
  [PerspectiveCamera](https://threejs.org/docs/pages/PerspectiveCamera.html).
- **Python OpenVR 2.12.1401**: optional dependency pinned in
  `requirements-live.txt`, installed only in this app's local environment.
  [Bindings source](https://github.com/cmbruns/pyopenvr).
- **Valve OpenVR**: native coordinate and tracking API definitions in the
  [official headers](https://github.com/ValveSoftware/openvr/blob/master/headers/openvr.h).
  Native coordinates are right-handed; Y is up, X is right, −Z is forward,
  and distances are metres. This app requests standing-origin poses with zero
  prediction and does not request compositor ownership or output commands.

All scene models in this app are simple procedural schematics. They are not
measured CAD models of the apparatus or a tracker visibility/occlusion simulation.

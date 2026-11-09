import * as T from "./vendor/build/three.module.js";
import { ReferenceRoom } from "./reference-room.js";
import { OrbitControls } from "./vendor/examples/jsm/controls/OrbitControls.js";
import {
    vec,
    point,
    rotation,
    screenPose,
    offAxis,
    hitScreen,
} from "./math.js";

const COLORS = {
    head: 0x167b84,
    weapon: 0xb36922,
    tracker: 0xf4be50,
    base: 0x6a6e95,
};
const unitBox = new T.BoxGeometry(1, 1, 1);
function box(group, size, position, color) {
    const mesh = new T.Mesh(
        unitBox,
        new T.MeshStandardMaterial({ color, roughness: 0.75 }),
    );
    mesh.scale.set(...size);
    mesh.position.set(...position);
    group.add(mesh);
    return mesh;
}
function line(group, color, points = 2) {
    const geometry = new T.BufferGeometry();
    geometry.setAttribute(
        "position",
        new T.BufferAttribute(new Float32Array(points * 3), 3),
    );
    const result = new T.Line(geometry, new T.LineBasicMaterial({ color }));
    result.frustumCulled = false;
    group.add(result);
    return result;
}
function updateLine(object, points) {
    const attr = object.geometry.getAttribute("position");
    points.forEach((p, i) => attr.setXYZ(i, p.x, p.y, p.z));
    attr.needsUpdate = true;
}
function place(object, pose) {
    object.position.copy(pose.p);
    object.quaternion.copy(pose.q);
}
function trackerModel() {
    const group = new T.Group();
    box(group, [0.09, 0.035, 0.09], [0, 0, 0], COLORS.tracker);
    for (let i = 0; i < 3; i++) {
        const angle = (i * Math.PI * 2) / 3;
        const lug = box(
            group,
            [0.035, 0.025, 0.045],
            [Math.sin(angle) * 0.055, 0, Math.cos(angle) * 0.055],
            0x3f4548,
        );
        lug.rotation.y = angle;
    }
    group.add(new T.AxesHelper(0.19));
    return group;
}

export class CalibrationScene {
    constructor(canvas, preview, labelHost) {
        this.canvas = canvas;
        this.preview = preview;
        this.labelHost = labelHost;
        this.roomContextLost = false;
        this.previewContextLost = false;
        for (const [surface, key] of [
            [canvas, "roomContextLost"],
            [preview, "previewContextLost"],
        ]) {
            surface.addEventListener("webglcontextlost", () => {
                this[key] = true;
            });
            surface.addEventListener("webglcontextrestored", () => {
                this[key] = false;
            });
        }
        this.renderer = new T.WebGLRenderer({ canvas, antialias: true });
        this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
        this.renderer.setClearColor(0xeeeae1);
        this.previewRenderer = new T.WebGLRenderer({
            canvas: preview,
            antialias: true,
        });
        this.previewRenderer.setPixelRatio(Math.min(devicePixelRatio, 2));
        this.previewRenderer.setClearColor(0x1f2930);
        this.scene = new T.Scene();
        this.scene.add(new T.HemisphereLight(0xffffff, 0x77847c, 2.4));
        const light = new T.DirectionalLight(0xffffff, 3);
        light.position.set(3, 8, 5);
        this.scene.add(light);
        this.camera = new T.PerspectiveCamera(43, 1, 0.02, 150);
        this.controls = new OrbitControls(this.camera, canvas);
        this.controls.minDistance = 0.4;
        this.controls.maxDistance = 25;
        this.controls.maxPolarAngle = Math.PI * 0.49;
        this.controls.target.set(0, 1.1, 1.1);
        this.setView("room");
        this.eyeCamera = new T.PerspectiveCamera();
        this.grid = new T.GridHelper(10, 20, 0xb1b3a9, 0xd1d1c7);
        this.scene.add(this.grid);
        this.floor = box(this.scene, [6, 0.03, 7], [0, -0.025, 1], 0xe6e3d9);
        this.roomBounds = new T.LineSegments(
            new T.EdgesGeometry(unitBox),
            new T.LineBasicMaterial({
                color: 0xbdc8bd,
                transparent: true,
                opacity: 0.45,
            }),
        );
        this.scene.add(this.roomBounds);
        this.screen = new T.Group();
        this.scene.add(this.screen);
        this.screenFrame = box(this.screen, [1, 1, 0.075], [0, 0, 0], 0x363d42);
        this.screenFace = box(
            this.screen,
            [1, 1, 0.008],
            [0, 0, 0.045],
            0x879d9a,
        );
        this.screenAxes = new T.AxesHelper(0.35);
        this.screen.add(this.screenAxes);
        this.screenStand = box(
            this.scene,
            [0.07, 1, 0.07],
            [0, 0.5, -0.04],
            0x6d7777,
        );
        this.screenFoot = box(
            this.scene,
            [0.7, 0.04, 0.4],
            [0, 0.02, -0.04],
            0x6d7777,
        );
        this.screenCross = [
            line(this.screen, 0xe5efeb),
            line(this.screen, 0xe5efeb),
        ];
        this.person = new T.Group();
        this.scene.add(this.person);
        this.torso = new T.Mesh(
            new T.CapsuleGeometry(0.22, 0.65, 8, 20),
            new T.MeshStandardMaterial({ color: 0x77a6a5, roughness: 0.9 }),
        );
        this.person.add(this.torso);
        this.head = new T.Group();
        this.scene.add(this.head);
        const skull = new T.Mesh(
            new T.SphereGeometry(0.13, 24, 16),
            new T.MeshStandardMaterial({ color: 0x528d90 }),
        );
        skull.scale.set(0.85, 1.1, 1);
        this.head.add(skull);
        box(this.head, [0.06, 0.035, 0.06], [0, 0, -0.13], 0x24626b);
        this.head.add(new T.AxesHelper(0.28));
        this.gun = new T.Group();
        this.scene.add(this.gun);
        // Schematic rifle: object origin is the receiver centre; bore points -Z.
        box(this.gun, [0.075, 0.12, 0.35], [0, 0, -0.04], 0x4c5458);
        box(this.gun, [0.065, 0.095, 0.28], [0, -0.015, 0.27], 0x303b40);
        box(
            this.gun,
            [0.045, 0.17, 0.075],
            [0, -0.12, 0.06],
            0x303b40,
        ).rotation.x = -0.25;
        box(
            this.gun,
            [0.052, 0.19, 0.08],
            [0, -0.12, -0.08],
            0x4c5458,
        ).rotation.x = 0.15;
        box(this.gun, [0.04, 0.04, 0.3], [0, 0.04, -0.36], 0x343f43);
        box(this.gun, [0.035, 0.04, 0.13], [0, 0.09, -0.04], 0x252f35);
        this.gunAxes = new T.AxesHelper(0.28);
        this.scene.add(this.gunAxes);
        this.headTracker = trackerModel();
        this.scene.add(this.headTracker);
        this.weaponTracker = trackerModel();
        this.scene.add(this.weaponTracker);
        this.bases = [0, 1].map(() => {
            const group = new T.Group();
            box(group, [0.11, 0.12, 0.1], [0, 0, 0], COLORS.base);
            box(group, [0.075, 0.075, 0.01], [0, 0, -0.055], 0x252b39);
            group.add(new T.AxesHelper(0.26));
            this.scene.add(group);
            return group;
        });
        this.baseStands = this.bases.map(() =>
            box(this.scene, [0.025, 1, 0.025], [0, 0.5, 0], 0xa4a5a5),
        );
        this.headRay = line(this.scene, COLORS.head);
        this.weaponRay = line(this.scene, COLORS.weapon);
        this.headMountLine = line(this.scene, COLORS.head);
        this.weaponMountLine = line(this.scene, COLORS.weapon);
        this.frustum = Array.from({ length: 4 }, () =>
            line(this.scene, 0x8babab),
        );
        this.hitMarker = new T.Mesh(
            new T.SphereGeometry(0.025, 16, 12),
            new T.MeshBasicMaterial({ color: COLORS.weapon }),
        );
        this.scene.add(this.hitMarker);
        this.labels = [
            ["screen", "Screen", this.screen],
            ["person", "Person", this.person],
            ["headTracker", "Head tracker", this.headTracker],
            ["weaponTracker", "Weapon tracker", this.weaponTracker],
            ["base0", "Base station 1", this.bases[0]],
            ["base1", "Base station 2", this.bases[1]],
        ].map(([id, text, object]) => {
            const el = document.createElement("span");
            el.textContent = text;
            el.className = `scene-label ${id}`;
            labelHost.append(el);
            return { el, object, id };
        });
        this.virtual = new T.Scene();
        this.virtual.background = new T.Color(0x202d35);
        this.virtual.add(new T.HemisphereLight(0xeefaff, 0x3c4754, 3));
        this.virtualRoot = new T.Group();
        this.virtual.add(this.virtualRoot);
        // Room geometry rests on world Y=0. The target alone is screen-relative.
        this.virtualRoom = new T.Group();
        this.virtual.add(this.virtualRoom);
        this.virtualFloor = new T.LineSegments(
            new T.BufferGeometry(),
            new T.LineBasicMaterial({ color: 0x668485 }),
        );
        this.virtualRoom.add(this.virtualFloor);
        this.virtualFloor.position.y = 0.004;
        this.referenceRoom = new ReferenceRoom(this.virtualRoom, this.virtualRoot);
        this.referenceProps = [
            box(this.virtualRoom, [0.22, 2, 0.22], [-1.25, 1, -2], 0x6aa3a5),
            box(this.virtualRoom, [0.5, 0.5, 0.5], [0.55, 0.25, -1.4], 0xc69c63),
            box(this.virtualRoom, [0.7, 1.1, 0.7], [-0.6, 0.55, -3.7], 0x6e8eae),
        ];
        for (const object of this.referenceProps)
            this.referenceRoom.addContactShadow(this.virtualRoom, object);
        this.target = new T.Group();
        this.virtualRoot.add(this.target);
        for (let i = 0; i < 3; i++) {
            const ring = new T.Mesh(
                new T.TorusGeometry(0.25 + i * 0.2, 0.018, 8, 64),
                new T.MeshBasicMaterial({
                    color: i === 0 ? 0xe6b963 : 0xa9c7c5,
                }),
            );
            this.target.add(ring);
        }
        box(this.target, [0.025, 0.12, 0.02], [0, 0, 0], 0xe6b963);
        box(this.target, [0.12, 0.025, 0.02], [0, 0, 0], 0xe6b963);
        this.targetStand = box(this.virtual, [0.025, 1, 0.025], [0, 0, 0], 0x8f9b98);
        this.targetFoot = box(this.virtual, [0.4, 0.025, 0.3], [0, 0.0125, 0], 0x8f9b98);
        this.virtualHit = new T.Mesh(
            new T.SphereGeometry(0.025),
            new T.MeshBasicMaterial({ color: 0xf6b85c }),
        );
        this.virtual.add(this.virtualHit);
        this.previewValid = false;
    }

    setView(view) {
        const target = this.controls.target;
        const offsets = {
            room: [4.4, 2.8, 5.4],
            front: [0, 1, 6],
            side: [6, 1, 0],
            top: [0.01, 7, 0.01],
        };
        this.camera.position
            .copy(target)
            .add(vec(offsets[view] || offsets.room));
        this.controls.update();
    }

    configure(config) {
        this.config = config;
        const s = config.screen,
            sp = screenPose(config);
        place(this.screen, sp);
        this.screenFrame.scale.set(s.width + 0.08, s.height + 0.08, 0.075);
        this.screenFace.scale.set(s.width, s.height, 0.008);
        this.screenStand.position.set(s.x, s.bottom / 2, s.z - 0.04);
        this.screenStand.scale.y = s.bottom;
        this.screenFoot.position.set(s.x, 0.02, s.z - 0.04);
        updateLine(this.screenCross[0], [
            vec([-s.width / 2, 0, 0.052]),
            vec([s.width / 2, 0, 0.052]),
        ]);
        updateLine(this.screenCross[1], [
            vec([0, -s.height / 2, 0.052]),
            vec([0, s.height / 2, 0.052]),
        ]);
        this.floor.scale.set(config.room.width, 0.03, config.room.depth);
        this.floor.position.z = config.room.depth / 2 - 1;
        this.roomBounds.scale.set(
            config.room.width,
            config.room.height,
            config.room.depth,
        );
        this.roomBounds.position.set(
            0,
            config.room.height / 2,
            config.room.depth / 2 - 1,
        );
        this.personHeight = config.person.height;
        this.torso.scale.set(
            config.person.radius / 0.22,
            Math.max(0.2, config.person.height - 0.4) / 1.09,
            config.person.radius / 0.22,
        );
        this.torso.position.y = (config.person.height - 0.4) / 2;
        this.gun.scale.setScalar(config.weapon.length / 0.9);
        config.baseStations.forEach((base, i) => {
            const object = this.bases[i];
            object.visible = base.enabled;
            object.position.fromArray(base.position);
            object.quaternion.copy(rotation(base.rotation));
            this.baseStands[i].visible = base.enabled;
            this.baseStands[i].position.set(
                base.position[0],
                base.position[1] / 2,
                base.position[2],
            );
            this.baseStands[i].scale.y = Math.max(0.001, base.position[1]);
        });
        this.scene.traverse((object) => {
            if (object instanceof T.AxesHelper)
                object.visible = config.view.axes;
        });
        place(this.virtualRoot, sp);
        this.virtualRoom.position.set(s.x, 0, s.z);
        const floorSize = `${config.room.width}:${config.room.depth}`;
        if (this.virtualFloorSize !== floorSize) {
            const halfWidth = config.room.width / 2;
            const depth = config.room.depth;
            const vertices = [];
            // Half-metre squares, plus exact boundaries for fractional room sizes.
            for (let x = -halfWidth; x < halfWidth; x += 0.5)
                vertices.push(x, 0, 0, x, 0, -depth);
            vertices.push(halfWidth, 0, 0, halfWidth, 0, -depth);
            for (let z = 0; z > -depth; z -= 0.5)
                vertices.push(-halfWidth, 0, z, halfWidth, 0, z);
            vertices.push(-halfWidth, 0, -depth, halfWidth, 0, -depth);
            this.virtualFloor.geometry.dispose();
            this.virtualFloor.geometry = new T.BufferGeometry();
            this.virtualFloor.geometry.setAttribute(
                "position", new T.Float32BufferAttribute(vertices, 3),
            );
            this.virtualFloorSize = floorSize;
        }
        // Follow the screen's horizontal facing while keeping the floor level,
        // even when the display itself is pitched or rolled.
        const screenNormal = vec([0, 0, 1]).applyQuaternion(sp.q);
        this.virtualRoom.rotation.y = Math.atan2(screenNormal.x, screenNormal.z);
        this.referenceRoom.configure(config);
        this.target.position.set(0, 0, -config.view.virtualDepth);
        const targetPosition = point(sp, [0, 0, -config.view.virtualDepth]);
        const standHeight = Math.max(0, targetPosition.y - 0.65);
        this.targetStand.visible = this.targetFoot.visible = standHeight > 0;
        this.targetStand.scale.y = standHeight;
        this.targetStand.position.set(targetPosition.x, standHeight / 2, targetPosition.z);
        this.targetFoot.position.set(targetPosition.x, 0.0125, targetPosition.z);
    }

    update(head, weapon) {
        const config = this.config;
        this.head.visible =
            this.person.visible =
            this.headTracker.visible =
                head.valid;
        this.gun.visible =
            this.weaponTracker.visible =
            this.gunAxes.visible =
                weapon.valid;
        this.gunAxes.visible = weapon.valid && config.view.axes;
        this.headRay.visible = head.valid && config.view.rays;
        this.weaponRay.visible = weapon.valid && config.view.rays;
        this.headMountLine.visible = head.valid;
        this.weaponMountLine.visible = weapon.valid;
        this.frustum.forEach(
            (l) => (l.visible = head.valid && config.view.frustum),
        );
        this.hitMarker.visible = this.virtualHit.visible = false;
        this.previewValid = false;
        let eye = null,
            hit = null;
        if (head.valid) {
            place(this.head, head.object);
            place(this.headTracker, head.tracker);
            this.person.position.set(
                head.object.p.x,
                Math.max(0, head.object.p.y - (config.person.height - 0.15)),
                head.object.p.z,
            );
            eye = point(head.object, config.head.eye);
            const forward = vec([0, 0, -1]).applyQuaternion(head.object.q);
            updateLine(this.headRay, [
                eye,
                eye.clone().addScaledVector(forward, 4),
            ]);
            updateLine(this.headMountLine, [head.object.p, head.tracker.p]);
            const sp = screenPose(config);
            [
                [-1, -1],
                [1, -1],
                [1, 1],
                [-1, 1],
            ].forEach(([x, y], i) =>
                updateLine(this.frustum[i], [
                    eye,
                    point(sp, [
                        (x * config.screen.width) / 2,
                        (y * config.screen.height) / 2,
                        0,
                    ]),
                ]),
            );
            this.previewValid = offAxis(this.eyeCamera, eye, config);
        }
        if (weapon.valid) {
            place(this.gun, weapon.object);
            place(this.gunAxes, weapon.object);
            place(this.weaponTracker, weapon.tracker);
            const muzzle = point(weapon.object, config.weapon.muzzle);
            const direction = vec([0, 0, -1])
                .applyQuaternion(rotation(config.weapon.bore))
                .applyQuaternion(weapon.object.q);
            hit = hitScreen(muzzle, direction, config);
            updateLine(this.weaponRay, [
                muzzle,
                hit?.world || muzzle.clone().addScaledVector(direction, 6),
            ]);
            updateLine(this.weaponMountLine, [
                weapon.object.p,
                weapon.tracker.p,
            ]);
            if (hit?.inside) {
                this.hitMarker.visible = config.view.rays;
                this.hitMarker.position.copy(hit.world);
            }
            // Show the bore/screen intersection at the screen plane in the preview.
            if (hit?.inside && this.previewValid) {
                this.virtualHit.visible = true;
                this.virtualHit.position.copy(hit.world);
            }
        }
        return {
            eye,
            hit,
            previewValid: this.previewValid && !this.previewContextLost,
            graphicsUnavailable: this.previewContextLost,
        };
    }

    draw() {
        this.controls.update();
        const w = this.canvas.clientWidth,
            h = this.canvas.clientHeight;
        if (w && h) {
            if (this.lastRoomSize !== `${w}:${h}`) {
                this.renderer.setSize(w, h, false);
                this.camera.aspect = w / h;
                this.camera.updateProjectionMatrix();
                this.lastRoomSize = `${w}:${h}`;
            }
            this.renderer.render(this.scene, this.camera);
            for (const { el, object, id } of this.labels) {
                const p = object.position.clone();
                p.y += id === "person" ? 0.7 : 0.18;
                p.project(this.camera);
                const visible =
                    this.config.view.labels &&
                    object.visible &&
                    Math.abs(p.x) < 0.92 &&
                    Math.abs(p.y) < 0.9 &&
                    p.z < 1;
                el.hidden = !visible;
                if (visible)
                    el.style.transform = `translate(${((p.x + 1) * w) / 2}px,${((-p.y + 1) * h) / 2}px) translate(-50%,-100%)`;
            }
        }
        const pw = this.preview.clientWidth,
            ph = this.preview.clientHeight;
        if (pw && ph) {
            if (this.lastPreviewSize !== `${pw}:${ph}`) {
                this.previewRenderer.setSize(pw, ph, false);
                this.lastPreviewSize = `${pw}:${ph}`;
            }
            if (this.previewValid)
                this.previewRenderer.render(this.virtual, this.eyeCamera);
            else this.previewRenderer.clear();
        }
    }
}

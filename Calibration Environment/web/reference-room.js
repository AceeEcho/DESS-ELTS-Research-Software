import * as T from "./vendor/build/three.module.js";

// Labels are world-sized so their apparent size supplies a distance cue.
function label(text) {
    const canvas = document.createElement("canvas");
    canvas.width = 512;
    canvas.height = 96;
    const context = canvas.getContext("2d");
    context.fillStyle = "#edf1eb";
    context.fillRect(0, 0, 512, 96);
    context.fillStyle = "#253b40";
    context.font = "600 34px sans-serif";
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.fillText(text, 256, 48);
    const texture = new T.CanvasTexture(canvas);
    texture.colorSpace = T.SRGBColorSpace;
    const mesh = new T.Mesh(
        new T.PlaneGeometry(0.6, 0.1125),
        new T.MeshBasicMaterial({ map: texture, side: T.DoubleSide }),
    );
    return mesh;
}
function panel(parent, color) {
    const mesh = new T.Mesh(
        new T.BoxGeometry(1, 1, 1),
        new T.MeshStandardMaterial({ color, roughness: 1 }),
    );
    parent.add(mesh);
    return mesh;
}

export class ReferenceRoom {
    constructor(parent, screenRoot) {
        this.floor = panel(parent, 0x87978a);
        this.walls = [panel(parent, 0x60777b), panel(parent, 0x60777b)];
        this.baseboards = [panel(parent, 0xbdc8b9), panel(parent, 0xbdc8b9)];
        this.backEdge = panel(parent, 0xbdc8b9);
        // An open doorway supplies familiar scale and keeps deeper targets visible.
        this.backWall = [panel(parent, 0x718486), panel(parent, 0x718486), panel(parent, 0x718486)];
        this.markers = [1, 2, 4].map((depth, index) => {
            const group = new T.Group();
            parent.add(group);
            const post = panel(group, 0xd2bd8a);
            post.scale.set(0.025, 1, 0.025);
            post.position.y = 0.5;
            const text = label(`${depth} m / ${(depth / 0.3048).toFixed(1)} ft floor depth`);
            text.position.set(0, 1.1, 0);
            group.add(text);
            return { group, depth, side: index % 2 === 0 ? -1 : 1 };
        });
        // A small reticle at zero depth is a fixed reference at the TV surface.
        this.screenMark = new T.Group();
        screenRoot.add(this.screenMark);
        for (const size of [[0.06, 0.003, 0.001], [0.003, 0.06, 0.001]]) {
            const bar = panel(this.screenMark, 0xf0ead7);
            bar.scale.set(...size);
            bar.material.dispose();
            bar.material = new T.MeshBasicMaterial({ color: 0xf0ead7 });
        }
        this.screenLabel = label("SCREEN PLANE · 0");
        this.screenLabel.scale.setScalar(0.45);
        screenRoot.add(this.screenLabel);
    }

    addContactShadow(parent, object) {
        // A soft, fixed contact patch anchors the static prop without a shadow
        // map or another render pass. It follows the measured floor plane.
        const canvas = document.createElement("canvas");
        canvas.width = canvas.height = 64;
        const context = canvas.getContext("2d");
        const gradient = context.createRadialGradient(32, 32, 4, 32, 32, 32);
        gradient.addColorStop(0, "rgba(15,25,25,0.55)");
        gradient.addColorStop(1, "rgba(15,25,25,0)");
        context.fillStyle = gradient;
        context.fillRect(0, 0, 64, 64);
        const shadow = new T.Mesh(
            new T.PlaneGeometry(1, 1),
            new T.MeshBasicMaterial({
                map: new T.CanvasTexture(canvas), transparent: true, depthWrite: false,
            }),
        );
        shadow.rotation.x = -Math.PI / 2;
        shadow.scale.set(object.scale.x * 1.8, object.scale.z * 1.8, 1);
        shadow.position.set(object.position.x, 0.002, object.position.z);
        parent.add(shadow);
    }

    configure(config) {
        const { width, depth, height } = config.room;
        this.floor.scale.set(width, 0.02, depth);
        this.floor.position.set(0, -0.01, -depth / 2);
        this.walls.forEach((wall, index) => {
            const side = index === 0 ? -1 : 1;
            wall.scale.set(0.02, height, depth);
            wall.position.set(side * (width / 2 + 0.01), height / 2, -depth / 2);
            const board = this.baseboards[index];
            board.scale.set(0.025, 0.1, depth);
            board.position.set(side * (width / 2 - 0.0125), 0.05, -depth / 2);
        });
        const doorWidth = Math.min(0.95, width * 0.6);
        const doorHeight = Math.min(2.03, height * 0.9);
        const wallWidth = (width - doorWidth) / 2;
        this.backWall.slice(0, 2).forEach((wall, index) => {
            wall.scale.set(wallWidth, height, 0.02);
            wall.position.set((index === 0 ? -1 : 1) * (doorWidth + wallWidth) / 2, height / 2, -depth - 0.01);
        });
        this.backWall[2].scale.set(doorWidth, height - doorHeight, 0.02);
        this.backWall[2].position.set(0, (height + doorHeight) / 2, -depth - 0.01);
        this.backEdge.scale.set(width, 0.025, 0.025);
        this.backEdge.position.set(0, 0.0125, -depth);
        this.markers.forEach(({ group, depth: markerDepth, side }) => {
            group.visible = markerDepth <= depth && width >= 1;
            group.position.set(side * Math.min(width * 0.25, config.screen.width * 0.42), 0, -markerDepth);
        });
        this.screenMark.position.set(-config.screen.width * 0.35, config.screen.height * 0.37, 0);
        this.screenLabel.position.set(-config.screen.width * 0.35, config.screen.height * 0.28, 0);
    }
}

// Worldcore with Unity
//
// Croquet Studios, 2020

// if building for old browsers, it seems we need core-js
// import 'core-js/stable'; // see https://github.com/parcel-bundler/parcel/issues/3308
import 'regenerator-runtime/runtime';

import { Session, Constants } from "@croquet/croquet";
import { ModelRoot, Actor, Pawn, mix, AM_Spatial, AM_Smoothed, AM_Avatar } from "./worldcore";
import { q_axisAngle } from "./worldcore/src/Vector";
import { PM_UnitySpatial, PM_UnitySmoothed, PM_UnityAvatar, UnityViewRoot, UnityViewStarter } from "./worldcore/src/UnityRender";

const Q = Constants;
Q.BALL_RADIUS = 0.2;
Q.BALL_SPEED = 3; // units per second
Q.GRAVITY = 1;
Q.BALL_LAUNCH_INTERVAL = 250;
Q.BALL_STEP_MS = 20;


//------------------------------------------------------------------------------------------
// Actor & Pawn
//------------------------------------------------------------------------------------------

class SpatialActor extends mix(Actor).with(AM_Spatial) { }
SpatialActor.register('SpatialActor');

class UnitySpatialPawn extends mix(Pawn).with(PM_UnitySpatial) { }
UnitySpatialPawn.register('UnitySpatialPawn');

class SmoothedActor extends mix(Actor).with(AM_Smoothed) { }
SmoothedActor.register('SmoothedActor');

class UnitySmoothedPawn extends mix(Pawn).with(PM_UnitySmoothed) { }
UnitySmoothedPawn.register('UnitySmoothedPawn');

class AvatarActor extends mix(Actor).with(AM_Avatar) { }
AvatarActor.register('AvatarActor');

class UnityAvatarPawn extends mix(Pawn).with(PM_UnityAvatar) {
    constructor(...args) {
        super(...args);
        this.listen('captureCamera', this.captureCamera);
    }

    captureCamera() {
        this.sendToUnity('attach_camera', { h: this.unityHandle });
    }
}
UnityAvatarPawn.register('UnityAvatarPawn');

class BallActor extends SmoothedActor {
    init() {
        super.init('UnitySmoothedPawn'); // includes creating the pawn, iff PawnManager is ready

        const r = max => Math.floor(max * this.random());
        const hsv = [r(360), r(50) + 50, 75];
        this.unityConfig = {
            type: "sphere",
            hsv
        };

        this.setLocation([-4, 0, -4]);
        this.setScale([Q.BALL_RADIUS, Q.BALL_RADIUS, Q.BALL_RADIUS]);
        const launchAngle = (Math.random() * 40 + 30) * Math.PI / 180; // 30 to 70 degrees up
        this.velocity = [Q.BALL_SPEED * Math.cos(launchAngle), Q.BALL_SPEED * Math.sin(launchAngle), 0];

        this.future(Q.BALL_STEP_MS).move();
    }

    move() {
        const deltaT = Q.BALL_STEP_MS / 1000;
        const loc = this.location.slice();
        loc[1] += this.velocity[1] * deltaT;
        if (loc[1] < 0) this.destroy();
        else {
            loc[0] += this.velocity[0] * deltaT;
            this.velocity[1] -= Q.GRAVITY * deltaT;
            this.moveTo(loc);
            this.future(Q.BALL_STEP_MS).move();
        }
    }
}
BallActor.register('BallActor');


//------------------------------------------------------------------------------------------
// UnitySessionModel
//------------------------------------------------------------------------------------------

class UnitySessionModel extends ModelRoot {
    init() {
        super.init();
        console.log("starting root!");

        this.userColors = [0, 60, 120, 180, 240, 300, 360].map(h => [h, 75, 75]);
        this.userRegistry = {};
        this.cameraOwner = null;
        this.subscribe(this.sessionId, 'view-join', this.addUser);
        this.subscribe(this.sessionId, 'view-exit', this.removeUser);

        this.subscribe('unity', 'reflectedUnityEvent', this.reactToReflectedEvent);

        this.createSceneObjects();
        this.tickSceneObjects();
        this.tickBallLauncher();
    }

    addUser(id) {
        if (this.userRegistry[id]) console.log(`user ${id} already registered`);
        else {
            const knownIndices = Object.values(this.userRegistry).map(record => record.userIndex);
            let userIndex = 0;
            while (knownIndices.includes(userIndex)) userIndex++;

            const avatarTracker = SpatialActor.create('UnitySpatialPawn');
            avatarTracker.unityConfig = {
                type: "cube",
                hsv: this.userColors[userIndex],
                alpha: 0.4
                };
            avatarTracker.setScale([0.5, 1, 0.5]);
            avatarTracker.setLocation([0, -0.75, 0]);
            const avatarHead = SpatialActor.create('UnitySpatialPawn');
            avatarHead.unityConfig = {
                type: "sphere",
                hsv: this.userColors[userIndex],
                alpha: 0.4
                };
            avatarHead.setScale([0.5, 0.5, 0.5]);
            const avatar = AvatarActor.create('UnityAvatarPawn');
            avatar.unityConfig = { type: "userAvatar" }; // signals to our Unity code to create an empty object that can be controlled by arrow keys
            avatar.addChild(avatarTracker);
            avatar.addChild(avatarHead);
            avatar.setLocation([userIndex - 1, 1.25, -7]);

            this.userRegistry[id] = { userIndex, avatar };
        }
        console.log({...this.userRegistry});
    }

    removeUser(id) {
        const userRecord = this.userRegistry[id];
        if (!userRecord) console.log(`user ${id} not found`);
        else {
            const { avatar } = userRecord;
            if (avatar === this.cameraOwner) {
                const msg = { selector: 'attach_camera', data: { h: null } };
                this.publish('unity', 'messageForUnity', msg);
                this.cameraOwner = null;
            }
            avatar.destroy(); // will also destroy any tracking child
            delete this.userRegistry[id];
        }
        console.log(this.userRegistry);
    }

    reactToReflectedEvent(event) {
        const { selector, data } = event;
        const id = data.userViewId;
        const userRecord = this.userRegistry[id];
        if (!userRecord) {
            console.error(`can't find user record for viewId ${id}`);
            return;
        }
        if (selector === 'test_trigger') {
            // a random thing to do: jump the avatar of the user who pressed Space
            // up or down.
            const avatar = userRecord.avatar;
            const loc = avatar.location.slice();
            loc[1] = 3.5 - loc[1];
            avatar.setLocation(loc);
        } else if (selector === 'capture_camera') {
            const avatar = userRecord.avatar;
            this.cameraOwner = avatar;
            avatar.say('captureCamera');
        }
    }

    createSceneObjects() {
        const floor = SpatialActor.create('UnitySpatialPawn');
        floor.unityConfig = {
            type: "cube",
            hsv: [ 20, 50, 75]
            };
        floor.setScale([100, 0.1, 100]);

        this.bars = [[], []]; // one set for each side (left and right)
        this.connectors = [[], []];

        const minBarLength = this.minBarLength = 0.25;
        this.maxBarLength = 1;
        for (let side = 0; side < 2; side++) {
            let lastBar;
            for (let i = 0; i < 5; i++) {
                const bar = SmoothedActor.create('UnitySmoothedPawn');
                bar.unityConfig = {
                    type: "cube",
                    hsv: [i & 1 ? 120 : 240, 75, 75]
                    };
                const width = 0.2 - i * 0.02;
                bar.setScale([ width, minBarLength, width ]);
                this.bars[side].push(bar);
                if (lastBar) {
                    const connector = SmoothedActor.create('UnitySmoothedPawn'); // empty object
                    connector.setLocation([ 0.2 * side - 0.1, minBarLength / 2 , 0]);
                    lastBar.addChild(connector);
                    bar.setLocation([0, minBarLength / 2, 0]);
                    connector.addChild(bar);
                    this.connectors[side].push(connector);
                } else bar.setLocation([0, minBarLength / 2, -4]);
                lastBar = bar;
            }
        }
        this.t = 0;
        this.stepDelta = 50;
    }

    tickSceneObjects() {
        this.t += this.stepDelta;
        const progressAngle = this.t * 2 * Math.PI / 10000;
        const sin = Math.sin(progressAngle);
        const sinSquared = sin * sin;
        const barLength = this.minBarLength + (this.maxBarLength - this.minBarLength) * sinSquared;
        const numBars = this.bars[0].length;
        const maxAngle = Math.PI / 2 / (numBars - 1);
        for (let side = 0; side < 2; side++) {
            for (let i = 0; i < numBars; i++) {
                if (i > 0) {
                    const connector = this.connectors[side][i - 1];
                    connector.moveTo([0, barLength / 2, 0]);
                    connector.rotateTo(q_axisAngle([0, 0, 1], sinSquared * (side || -1) * maxAngle));
                }
                const bar = this.bars[side][i];
                const barSize = bar.scale.slice();
                barSize[1] = barLength;
                bar.scaleTo(barSize);
                bar.moveTo([0, barLength / 2, i === 0 ? -4 : 0]);
            }
        }
        this.future(this.stepDelta).tickSceneObjects();
    }

    tickBallLauncher() {
        BallActor.create();
        this.future(Q.BALL_LAUNCH_INTERVAL).tickBallLauncher();
    }
}
UnitySessionModel.register("UnitySessionModel");

//------------------------------------------------------------------------------------------
// UnitySessionView
//------------------------------------------------------------------------------------------

class UnitySessionView extends UnityViewRoot {
    constructor(model) {
        console.log("starting app view root");
        super(model);
        if (model.cameraOwner) model.cameraOwner.say('captureCamera');
    }
}

async function go() {
    let lastStep = 0;
    UnityViewStarter.setViewRoot(UnitySessionView);
    const session = await Session.join("wc-unity13", UnitySessionModel, UnityViewStarter, { step: "manual", tps: "20x5", expectedSimFPS: 0 });
    const step = () => {
        const now = Date.now();
        if (now - lastStep > 15) {
            session.step(now);
            lastStep = now;
        }
        };
    const controller = session.view.realm.island.controller;
    controller.tickHook = () => Promise.resolve().then(step);
}

go();

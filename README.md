
# Croquet-driven Unity demo

Copyright (C) 2020 Croquet Corporation

This repository contains a demonstration of a Croquet app creating and controlling objects in a Unity scene.

# Installing the build environment for the Croquet app

Clone this repository, then in directory `worldcore`

    npm install

and then in the top directory again run

    npm install

then, to build the Croquet app,

    npm run build

If all is well, this will build `dist/index.html`.

# Installing the built Croquet app into the Unity project

Start the Unity project `Croquet Unity`.  Load the `Croquet Sample Scene` from the `Scenes` folder.

Open the `StreamingAssets` folder under Project/Assets, delete any `index.html` that is already there, then copy the `index.html` built for Croquet into this folder.

Press the run button in the Unity editor.  After a few seconds, a scene of moving objects should appear.

Use the arrow keys to move (up/down) or rotate (left/right) the simple avatar that appears in the scene.  The space bar will jump the avatar up or down.  The 'c' key will capture the scene camera, attaching it to the avatar's spherical "head".

# Building a Unity app, so multiple users can join the same session

_Because of limitations in the UniWebView and WebRTC packages, right now only a MacOS build will work._

Proceed with the normal Unity build process (through File -> Build Settings...), ensuring that the Target Platform is set to "Mac OS X".  Opening the resulting `.app` should present the same view as when running the app in the editor.

Trying to open the same app multiple times won't create multiple instances.  But the app can be duplicated (command-D), and each new copy will run as a separate instance.  The app can also be sent to colleagues to run on their own computers.  All copies will automatically participate in the same session.

You should see a new user avatar appearing as each instance of the app starts up.  Each instance interprets the arrow keys to move its own avatar, and whoever last pressed the 'c' key is the one who controls the scene camera.  In the basic demo, if the user in control of the camera closes their app instance, the camera will return to its default "home" position.  But there is no way for a user to explicitly release the camera.  An exercise for the reader.

# Opening a debugger on the Croquet session

In the Unity code as uploaded to the repository, the WebView in which the Croquet session runs is created with zero size, and immediately hidden so that it doesn't grab focus from the Unity app.  But if you need to check or debug the Croquet process, the WebView can be shown and its developer tools opened.

Comment out the following lines in `CroquetBridge.cs`:

```
Rect wvFrame = new Rect(0, 0, 0, 0);
bool hideWV = true;
```

and uncomment these:

```
UniWebView.SetWebContentsDebuggingEnabled(true);
Rect wvFrame = new Rect(0, 0, 300, 300); // top left of screen, arbitrary size
bool hideWV = false;
```

This specifies that, when the Unity app starts up, the WebView should appear as a small window at top left of the desktop, and that it should provide a debugging menu.  Right-clicking on the WebView, and selecting `Inspect Element` in the menu, will open its JavaScript developer tools.

# Alternative debugger-friendly Croquet build process: serving through Parcel

Even with the WebView and its developer tools available, repeatedly building the Croquet app and installing it in the StreamingAssets folder will become laborious.  A more efficient alternative is to direct the WebView to load the Croquet app from a localhost web address.

Instead of running

    npm run build

to produce `dist/index.html`, run

    npm start

to bundle the Croquet code into a file that (based on default settings in `package.json`) will be served on `http://localhost:9009`.

Then in Unity's `CroquetBridge.cs`, replace the line

```
string loadUrl = UniWebViewHelper.StreamingAssetURLForPath("index.html");
```

with the line

```
string loadUrl = "http://localhost:9009/index.html";
```

This will cause the WebView to load that page.  Opening the WebView's developer tools will now reveal the source code for the demo (and for the "worldcore" extension to Croquet, that this demo uses).

Because the `npm start` keeps running, watching for changes, the page will also automatically be rebuilt and reloaded whenever changes to the demo source (`wc-unity.js`) are saved.

# Anatomy of the Croquet code

The Croquet app, defined in `wc-unity.js`, conforms to the basic rules of Croquet as laid out at `https://croquet.io/sdk/docs/`, in terms of separation of Model (application state, that Croquet ensures is perfectly replicated for all clients of a session) and View (each client's local display, generated from the Model and responsive to local interactions).  It also draws on the as-yet unannounced "Worldcore" extension to Croquet, which simplifies the setup and management of active objects that have a Model part (referred to as an Actor) and a corresponding View part (referred to as a Pawn).

## Actors, Pawns

Actors and Pawns are designed to get their behaviours using Croquet's home-grown form of mixin.  For example, `AM_Spatial` is an Actor Mixin (hence the "AM_") that manages a 3D spatial location (along with rotation and scale) for its actor.  Correspondingly, there is a `PM_UnitySpatial` Pawn mixin that defines the behaviour for using the spatial information published by an `AM_Spatial` actor to drive the transform of a Unity GameObject.

When creating an Actor instance, application code specifies the name of the class that is to be instantiated to create the actor's corresponding pawn.  In our application, for example:

```
const avatarTracker = SpatialActor.create('UnitySpatialPawn');
```

...which uses the following two class definitions:

```
class SpatialActor extends mix(Actor).with(AM_Spatial) { }
SpatialActor.register('SpatialActor');

class UnitySpatialPawn extends mix(Pawn).with(PM_UnitySpatial) { }
UnitySpatialPawn.register('UnitySpatialPawn');
```

Note that nothing in the `SpatialActor` definition ties it to Unity.  This is intentional, in that nothing about the actor's behaviour - its movements, rotations, etc - needs to take account of the fact that it is going to be represented visually by a Unity object (as opposed to, say, a `ThreeJS` object, or any other form of 3D rendering).  The connection to Unity is established by the `UnitySpatialPawn` class mixing in `PM_UnitySpatial`.

That said, since only the Actor (not the Pawn) is part of the Model state that will be shared among all the users in a session, the Actor has to hold any properties that the Pawn needs in order to define - in this case, to Unity - how the Actor should be presented.  Continuing the `avatarTracker` example:

```
avatarTracker.unityConfig = {
    type: "cube",
    hsv: this.userColors[userIndex],
    alpha: 0.4
    };
```

All `PM_Unity...` pawns look for the `unityConfig` property on their actor, and communicate its sub-property values to Unity.  Currently the following properties are defined:

`type` determines what kind of GameObject will be created.  Recognised values are `cube` (the default, if no type is specified), `sphere`, `cylinder`, `empty`, and `userAvatar`.  The `userAvatar` type should be generated exactly once per user in the session.  These objects are manifested on the Unity side as empty GameObjects (though they are free to have other, visible, objects as children); in the default implementation of the Croquet bridge, each user's instance of Unity converts arrow key presses into movement of the user-avatar instance for its own user.

`hsv` and `alpha` are used to set colour and transparency of the Unity object.

Continuing:

```
avatarTracker.setScale([0.5, 1, 0.5]);
avatarTracker.setLocation([0, -0.75, 0]);
```

These set the scale and the position of the new object.  See `worldcore/Mixins.js` for the definition of `AM_Spatial` and its siblings `AM_Smoothed` and `AM_Avatar` to understand the movement methods that are available.

In due course:

```
const avatar = AvatarActor.create('UnityAvatarPawn');
avatar.unityConfig = { type: "userAvatar" }; // signals to our Unity code to create an empty object that can be controlled by arrow keys
avatar.addChild(avatarTracker);
```

This creates the (empty) Unity object that can be steered by the user, and sets the tracker as its child.  Again, `worldcore/Mixins.js` is the place to find the definition of the `addChild` behaviour (in the `AM_Tree` mixin, from which `AM_Spatial` and others inherit).

## The root model: UnitySessionModel

As explained at `https://croquet.io/sdk/docs/index.html` and in its attached tutorials, every Croquet app needs a root model that is responsible for creating and marshalling all shared state in the app.  For our demo, that is `UnitySessionModel`.  (Because this demo uses Worldcore, the root model is a subclass of `ModelRoot` rather than directly of `Model`.  This is purely a housekeeping difference.)

One of the Unity-related jobs of the `UnitySessionModel` is to receive and process (or forward) events generated by the users' Unity sessions.  To understand how these messages arrive, we first need a little detour behind the scenes to the role of the `UnityRenderManager` (URM; defined in `worldcore/UnityRender.js`), a View-side object that controls, for a single user, all communication between that user's Croquet session (running in the WebView) and Unity.

When some user A interacts with his or her Unity scene - for example, pressing the space bar to trigger a test action, which in this demo results in jumping that user's avatar up or down - here is the flow of events that takes place:

1. User A's Unity code (in `CroquetBridge.cs`) sends a message (in this case, `test_trigger`) to Croquet.  The message is received by A's URM.
2. The URM could act directly on the incoming message, for example by telling Unity to do something in turn, but that would only affect A's instance.  Instead, the URM has code of the following form:

    ```
    msg.data.userViewId = this.bootstrapView.viewId;
    this.publish('unity', 'reflectedUnityEvent', msg);
    ```

    This code adds a `userViewId` property to the message, identifying user A in terms of his/her client `viewId`, then publishes it as an event.

3. _Because there is a subscription to the `reflectedUnityEvent` event in a Model (in this case, the `UnitySessionModel`)_, the event goes out over the internet to the Croquet reflector handling this session, and is reflected back not just to the original sender but to all session clients.

4. Every user's `UnitySessionModel` receives the reflected event at [practically] the same instant.

    Here is how the `UnitySessionModel` is set up to handle such events:

    ```
    class UnitySessionModel extends ModelRoot {
        init() {
            super.init();
            this.cameraOwner = null;
            ...
            this.subscribe('unity', 'reflectedUnityEvent', this.reactToReflectedEvent);
        }

        reactToReflectedEvent(event) {
            const { selector, data } = event;
            const id = data.userViewId;
            const userRecord = this.userRegistry[id];
            if (selector === 'test_trigger') {
                // jump the avatar of the user who pressed Space up or down.
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
    ```

    The appropriate handling of the event depends on its selector.  A `test_trigger` event is handled by looking up the user avatar of the user identified in the event, calculating a new position for it, and telling the avatar to set its position accordingly.

5. The avatar actor responds to the `setLocation` by publishing a position-update event (the details are not our concern) that is subscribed to by its own pawn.
6. The pawn sends to Unity (via the URM) a position-update message, identifying itself with a handle that was assigned on pawn creation.
7. Unity receives the message, looks up the corresponding GameObject using the handle, and updates that object's transform.


The `reactToReflectedEvent` code shown above also includes the handling of the `capture_camera` event, which is a custom event created for this demo.

In this case, the Model takes note of which avatar now has the camera (this information will be needed for any new clients who might join the session).  It then has to get a message to Unity, as in the sequence above.  However, in this case there is no standard behaviour such as `setLocation` that will generate the necessary events and messages.  Instead, for this demo we set up the avatar's pawn class as follows:

```
class UnityAvatarPawn extends mix(Pawn).with(PM_UnityAvatar) {
    constructor(...args) {
        super(...args);
        this.listen('captureCamera', this.captureCamera);
    }

    captureCamera() {
        this.sendToUnity('attach_camera', { h: this.unityHandle });
    }
}
```

This shows that the pawn subscribes to `captureCamera` events coming from its own actor, and handles them (in the `captureCamera()` method) by explicitly sending to Unity a message that [again, custom] code on the Unity side will know how to handle.  In this case, Unity will attach the scene camera as a child of the GameObject representing this pawn.

So all the `UnitySessionModel` has to do to trigger this communication is tell the relevant avatar Actor to `avatar.say('captureCamera')`.

And, again, the crucial point is that this communication sequence - from `UnitySessionModel` to avatar Actor, to avatar Pawn, to Unity, to Unity GameObject - will be happening identically, and at the same time, in every user's view.


# Anatomy of the Unity code

The current demo makes use of the following objects and scripts:

`Croquet Bridge` (CroquetBridge.cs) - the manager for all Croquet-driven object creation and interaction, in collaboration with the UnityRenderManager in a Croquet session.  It is responsible for setting up the WebView that runs the Croquet JavaScript code (using the imported `UniWebView` asset), and the data channel (below) for high-throughput communication between the WebView and Unity.

`Data Channel` (CroquetDataChannel.cs) - the object responsible for establishing and maintaining communication with Croquet.  It calls on the services of the `WebRTC` package to build a WebRTC data channel.  Initial channel negotiation is carried out at the initiative of the Unity end, using as a negotiation side channel the primitive communication channels available through `UniWebView`.

`Croquet Object` (CroquetObject.cs) - a component dealing with Croquet-relevant properties and behaviours for each GameObject representing a Croquet pawn.

In addition, the minimal custom behaviour implemented for the demo is defined in...

`Croquet Bridge Test` (CroquetBridgeTest.cs; subclass of CroquetBridge) - defines `customCreate` and `customMessage` handlers that let it intervene in, respectively, the creation of Croquet Objects and the handling of messages, to support the custom behaviours (defined in CroquetObjectTest) of a `userAvatar` object for this demo.

`Croquet Object Test` (CroquetObjectTest.cs; subclass of CroquetObject) - adds the `AttachCamera` behaviour to a Croquet Object.


# What next?

Talk to us about what you need!

* Aran Lunzer `aran@croquet.io`

* David A Smith `david@croquet.io`


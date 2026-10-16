# Skyfall Hero

A third-person superhero sandbox built in Unity 6. Fly over a living city, smash through buildings, burn holes with heat vision, lift cars and planes, and fly straight up through the clouds into space and onto the Moon.

![Standing above a golden sea of clouds](Screenshots/space/0a_golden_cloud_sea.png)

## Features

- **Flight**: take off, hover, boost, and super-speed sprint with afterimages and speed lines
- **Destructible city**: punch, kick, ram or laser holes clean through buildings
- **Heat vision**: a beam that burns, scorches the ground and cuts through cars and planes
- **Super strength**: grab and throw people, cars and even airplanes
- **Living city**: traffic, pedestrians and air traffic that react to you
- **Space**: climb above the clouds, see the Earth, land on the Moon, fight a fighter squadron, then dive back home with re-entry fire
- **Mobile ready**: on-screen touch controls for iOS

## Scenes

### The city

| | |
|---|---|
| ![Superhero landing cracks the road](Screenshots/fx/landing.png) | ![Super speed afterimages](Screenshots/fx/superspeed.png) |
| ![Lifting a car over the street](Screenshots/life/7_lifting_car.png) | ![Heat vision blows up a car](Screenshots/life/9_laser_car.png) |

### Destruction

| | |
|---|---|
| ![Heat vision burns through a building](Screenshots/play/5_laser_through.png) | ![Punch and kick holes in a wall](Screenshots/play/3_punch_and_kick_holes.png) |

### Flight

| | |
|---|---|
| ![Boosting low over the sea](Screenshots/cape/3_fly_boost.png) | ![Holding up an airliner](Screenshots/life/10_holding_plane.png) |

### Space

| | |
|---|---|
| ![Above the Earth](Screenshots/space/5_city_and_earth.png) | ![Re-entry fire](Screenshots/space/6b_reentry_fire.png) |
| ![Earth from the Moon](Screenshots/space/9c_earth_from_the_moon.png) | ![Diving home](Screenshots/space/13a_dive_home_high.png) |

## Controls

### Keyboard and mouse

Click the Game view to capture the mouse. Press **Esc** to release it.

| Action | Key |
|---|---|
| Move | W A S D |
| Look | Mouse |
| Sprint / flight boost | Left Shift |
| Walk | Left Alt |
| Jump | Space |
| Take off / stop flying | F (or double-tap Space) |
| Fly up / down | Space / Left Ctrl or C |
| Super speed | R |
| Heat vision (hold) / throw held object | Left mouse |
| Punch | Q |
| Kick | E |
| Grab / drop | G |

A gamepad also works.

### Touch (mobile)

On phones, an on-screen joystick and buttons cover the same actions.

![Touch controls](Screenshots/space/14_touch_controls.png)

## Running it

1. Install **Unity 6000.6.4f1** (Unity 6) through Unity Hub.
2. Clone this repo and open the folder in Unity Hub.
3. Open `Assets/Scenes/City.unity` and press Play.

## Built with

- Unity 6 (URP) and the Input System
- C#: 51 scripts covering the hero, combat, destruction, city life, space and editor tools that build the world

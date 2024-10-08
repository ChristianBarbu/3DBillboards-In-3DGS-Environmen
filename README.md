# 3DBillboards-In-3DGS-Environment
This is an adaptation of Aras Pranckevičius implementation of the rendering part of 3DGS. It employs some additional changes to facilitate the interpolation between two Gaussian scenes. However, the scenes need to be prepared for this to work properly.
It seems that the project files are too large, you can download the project here: [thesis_project](https://1drv.ms/f/s!AjwojfWkJq7XhZJUJk92Xx8pWB1Jbw?e=8GhF5b). The project has been adapted in the Editor Version 2022.3.35f, it is in ```projects/GaussianExample```, i.e. there is no HDRP or URP support for that adaptation yet.

<p align="center">
  <a href="github_assets/"><i>Thesis</i></a>
</p>

## Models used in Thesis
As the assets and PLY files are generally very large, here is a Onedrive link, where the PLY files, as well as the already converted files for Unity have been placed:
[PLY Files And Unity-ready Assets](https://1drv.ms/f/s!AjwojfWkJq7XhZIPUx2OSIUwdERsJQ?e=JZdycG)

## Usage
### Successive Gaussian Training

Before starting with the successive gaussian training, make sure you understand [3dgs by graphdeco-inria](https://github.com/graphdeco-inria/gaussian-splatting). In the process of creating the COLMAP data cameras should be fixed, to keep the scenes aligned. The following illustration
gives an idea of the workflow used:

![Successive COLMAP](github_assets/Successive_COLMAP_downscaled.png)

The following commands can be used for successive training.
1. Pass:

```shell
python train.py -s <path to first COLMAP data> --iterations N --densify_until_iter N -m <first output> -r 4
```

2. Pass:
```shell
python train.py -s <path to second COLMAP data> --iterations M --densify_until_iter N -m <second output> --start_checkpoint <path to first COLMAP data>\chkpntN.pth -r 4
```
Note, that for this project $M=2\cdot N = 16.000$

### Loading successive gaussian scenes into Unity

1. Tick the checkbox ```GenerateAlignedAsets```
2. First input PLY file is the one to which the second one will be aligned in terms of the Morton Order

Note: Quality settings stick to the quality of the first object. If you have optimized your models like explained, then the number of splats should be the same.

![GaussianAssetCreator](github_assets/GaussianSplatCreator.png)

### Preparing the Scene

The movement of the user's head will move three cameras synchronously:
- BillboardAndCameras/VirtualWindow (Billboard)/BillboardCamera
- BillboardAndCameras/VirtualWindow (InteriorObjects)/InteriorObjectsCamera
- WallCamera/ProxyParent/ProxyCameraObject

Make sure that the front face of the billboard has the same normal as the wall to which you want to project the billboard in your 3DGS scene.
The CameraMovementController synchronizes the movement of the objects, given a subject and some followers.

### Head Tracking

For tracking the head you can use AITrack, in combination with OpenTrack. The UDP Receiver in Unity is responsible for getting the data.
You can find that responsible object in the hierarchy:
- UTILITY_OBJECTS/UDPReceiver
The object that will be moved is the ```Target Object```

## Demos
### Interpolation Demo

This interpolation demo shows the results, which have been achieved by using the successive 3DGS optimization process mentioned above. The position of the gaussians is fixed and we interpolate between the rest of the ViewData.

![InterpolationDemo](github_assets/interpolation_demo.gif)

### Billboard Demo

This is a demo for the 3D billboard. It can be used in combination with the head tracker for achieving an enhanced 3D effect.

![3DBillboardDemo](github_assets/3DBillboard_demo.gif)

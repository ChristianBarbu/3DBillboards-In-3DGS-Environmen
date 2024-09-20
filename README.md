# 3DBillboards-In-3DGS-Environment
This is an adaptation of Aras Pranckevičius implementation of the rendering part of 3DGS. It employs some additional changes to facilitate the interpolation between two Gaussian scenes. However, the scenes need to be prepared for this to work properly.
It seems that the project files are too large, you can download the project here: [thesis_project](https://1drv.ms/f/s!AjwojfWkJq7XhZJUJk92Xx8pWB1Jbw?e=8GhF5b). The project has been adapted in the Editor Version 2022.3.35f.

## Models used in Thesis
As the assets and PLY files are generally very large, here is a Onedrive link, where the PLY files, as well as the already converted files for Unity have been placed:
[PLY Files And Unity-ready Assets](https://1drv.ms/f/s!AjwojfWkJq7XhZIPUx2OSIUwdERsJQ?e=JZdycG)

## Successive Gaussian Training
The following commands can be used for successive training.
1. Pass:

```shell
python train.py -s <path to first COLMAP data> --iterations N --densify_until_iter N -m <first output> -r 4
```

2. Pass:
```shell
python train.py -s <path to second COLMAP data> --iterations M --densify_until_iter N -m <second output> --start_checkpoint <path to first COLMAP data>\chkpntN.pth -r 4
```
## Loading successive gaussian scenes into Unity

1. Tick the checkbox ```GenerateAlignedAsets```
2. First input PLY file is the one to which the second one will be aligned in terms of the Morton Order

Note: Quality settings stick to the quality of the first object. If you have optimized your models like explained, then the number of splats should be the same.

![GaussianAssetCreator](github_assets/GaussianSplatCreator.png)

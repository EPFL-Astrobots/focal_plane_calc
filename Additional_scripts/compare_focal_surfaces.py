#!/usr/bin/env python
# -*- coding: utf-8 -*-
import sys
# Or the path to the focal_plane_calc folder on your machine
# I know it is not the best way to do it but it works for now, To Be Fixed Later
sys.path.insert(0,'C:/Users/rombach/Documents/Astrobots/Inosuisse/focal_plane_calc')
import numpy as np
import matplotlib.pyplot as plt
from parameters import FocalSurf
from parameters import SavingResults

projects = ['MUST', 'MegaMapper', 'Spec-s5']
save_plots = False
saving_df = {'save_plots': save_plots}
save = SavingResults(saving_df)

for proj in projects:
    surf = FocalSurf(project = proj)
    optics_data = surf.read_focal_plane_data() # Read data from csv file
    plt.plot(optics_data['R'],-optics_data['Z'], label = f"{proj} - BFS = {surf.BFS:.0f} mm")

plt.xlim(xmin=0)
plt.xlabel('R [mm]')
plt.ylabel('Z [mm]')
plt.legend()
plt.title('Focal plane comparison')
plt.grid()
save.save_figures_to_dir('focal_plane_comparison', 'png')


plt.figure()
project = "Spec-s5"
surf = FocalSurf(project = project)
optics_data = surf.read_focal_plane_data() # Read data from csv file
plt.plot(optics_data['R'],-np.rad2deg(optics_data['Slope']), label = "Asph")
plt.plot(optics_data['R'],-np.rad2deg(optics_data['BFS_Slope']), label = "BFS")
plt.grid()
plt.xlabel('R [mm]')
plt.ylabel('Slope [rad]')

plt.show()
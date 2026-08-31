# Flumeria Voice Analyzer UI Transition Issue

## Issue Description

After a successful login, when the user enters the main Flumeria Studio
page, the Voice Analyzer initially displays Lucy's picture inside a
circular frame (Figure 1).

![Figure 1 - Lucy's picture displayed in the Voice Analyzer after
login](images/image1.jpg)

## Observed Behavior

After approximately one second or less, the Voice Analyzer unexpectedly
changes from Lucy's picture to a sphere representation as shown in
Figure 2.

![Figure 2 - Sphere representation displayed after the initial
transition](images/image2.png)

After the map is loaded into the viewer, the sphere changes again into
another sphere representation shown in Figure 3.

![Figure 3 - Final sphere representation after map
loading](images/image3.png)

## Problem Statement

The transition between these three visual states is incorrect. The Voice
Analyzer should not switch between different representations during the
user session.

## Expected Behavior

Once the login process is completed, the Voice Analyzer should
consistently use the sphere representation shown in Figure 2 as Lucy's
analyzer interface until the application is closed.

## Required Fix

-   Remove the unnecessary visual transitions after login.
-   Keep the Figure 2 sphere representation as the permanent Voice
    Analyzer state throughout the application lifecycle.
-   Ensure that loading the map viewer does not replace the analyzer
    representation.

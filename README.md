# NavMeshAreaCustomizer
Created by **Adam Jůva**
- Website -> [https://adamjuva.com/](https://adamjuva.com/)
- Twitter -> [https://twitter.com/AdamJuva](https://twitter.com/AdamJuva)
- LinkedIn -> [https://www.linkedin.com/in/adamjuva/](https://www.linkedin.com/in/adamjuva/)

If you find this project helpful, you can support me by a small [donation](https://www.paypal.com/donate/?hosted_button_id=SWDA22AH63KWJ).

![Donation](https://adamjuva.com/wp-content/uploads/2020/07/Donation.png)

# Introduction

NavMesh Area Customizer is tool for Unity game engine to customize and select exact area where NavMesh is generated.

Demo Video: https://youtu.be/X7N42Abx8Mo

# Guide
1. Create empty game object and add NavMeshAreaCustomizer component to it.
2. Select NavMeshAreaCustomizer and click on “Add Segment” button.
3. Assign mesh filter and collider of your terrain mesh on which area should be generated to
corresponding fields of this newly created AreaSegment component.
4. Customize AreaSegment by draging Points (child game objects of AreaSegment) on your terrain
and create your own area.
5. In case of concave shape, use multiple AreaSegments in convex shape and combine them
together to create concave shaped area.
6. After shape for NavMesh area is completed, select NavMeshAreaCustomizer component and
click on button “Calculate Area”.
7. Do not check “Navigation Static” on your terrain game object, “Navigation Static” is
automatically enabled only on AreaSegment game objects.
8. Bake your NavMesh.
9. For even more control (baking your NavMesh during runtime, etc.) use in combination with
[NavMeshComponents](https://github.com/Unity-Technologies/NavMeshComponents).

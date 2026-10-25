// The real-window tests (#10, #11) share one desktop: parallel classes steal the foreground,
// move the cursor and show/kill probe windows under each other (AC27 saw a foreign
// foreground change). Run the classes in this assembly one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

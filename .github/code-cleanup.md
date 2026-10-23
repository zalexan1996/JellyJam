We will be adding an iPhone port in the future, but for now we need to cleanup the code organization of the JellyJam.Android project.

## My Principles
- I value clean code that properly abides by SOLID
- I make heavy use of dependency injection and proper separation of UI from the underlying services
- My code should read like fine prose. I like abstracting logic into functions, but within reason. I hate microfunctions. Functions can be, at most, 20 statements long or 25 lines long unless there really is no better way.
- I make extensive use of elegant design patterns and data structures


## Cleanup Steps
- Rename the JellyJam.Android project to JellyJam.MAUI
- Create Styles.xaml file that stores multiple ResourceDictionaries. App.xaml will use the JellyJamCyber style. This will represent the current theme
- Create interfaces for the components in src\JellyJam.Android\Platforms\Android. Keep in mind that these interfaces will need implementations for iPhone later, so keep the inputs and outputs platform generic. 
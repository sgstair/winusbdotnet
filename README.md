winusbdotnet
============

This project is a C# wrapper for using WinUSB to speak to USB devices. It's still very much a work in progress as its development is driven largely by projects my friends and I are working on. Eventually it will be made more friendly, but it's already a very powerful tool for rapidly developing USB interface code.

Some current highlights:
* The Devices library includes primitive support for controlling [Fadecandy](https://github.com/scanlime/fadecandy), and an early driver for interfacing the Seek Thermal camera (android version)
* TestSeek is a simple application which acquires and displays frames from the Seek Thermal camera. It will eventually be moved into a different git project. See the readme in the [TestSeek](https://github.com/sgstair/winusbdotnet/tree/master/TestSeek) folder for more details on how to use it.


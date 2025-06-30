# ElevationWebApi

A .net core elevation service that uses memory mapped hgt files or in memory files to return elevation information for a given latitude and longitude array.

To build the docker image: 
```
docker build -t elevationwebapi .
docker run -p 12345:8080 -v /hgt-files-folder:/app/elevation-cache elevationwebapi 
```

Now surf to `localhost:12345/swagger/` to get a simple UI to interact with the elevation service

This service was tested on the entire world with memory mapped files and on a small country with in memory files.

This service supports both GET and POST methods for getting the elevation.
For in memory provider you can place in the elevation folder zip or bz2 compressed files and the service will decompress them when booting up.

This docker image is also available on docker hub: `israelhikingmap/elevationwebapi`

You can choose between two types of elevation providers by specifying the `ELEVATION_PROVIDER` environment variable:
`MMAP` - For memory mapped elevation provider
Default - In memory elevation provider

The memory mapped elevation provider is using a cache to store the files.
You can change the cache sliding window timeout using `CACHE_SLIDING_WINDOW` environment variable.
The default is 30 and the units are minutes.
This means that if a file is not used in the last 30 minutes it will be evacuated from the cache.


# Elevation Micro Service

A .net core elevation service that uses memory mapped hgt files to return elevation information for a given latitude and longitude array.

To build a docker image: 
```
cd src
docker build -t elevation-ms .
docker run -p 12345:80 elevation-ms
```

Now surf to `localhost:12345/swagger/` to get a simple UI to interact with the elevation service

This repository only hold elevation for Israel, which is a small country and the implementation he might be enough, it might not be the cause for bigger countries.
If you would like to see if it works change the zip file in the elevation-cache folder to your hgt zip/bz2 files.

This docker file is also available on docker hub: `israelhikingmap/elevation`



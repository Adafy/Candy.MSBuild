echo Requires choco install nuget.commandline
rmdir Output /Q /S nonemptydir
mkdir Output
nuget pack Candy.MSBuild.nuspec -OutputDirectory Output
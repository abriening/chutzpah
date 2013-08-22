properties {
  $baseDir = Resolve-Path .
  $configuration = "debug"
  $xUnit = Resolve-Path .\3rdParty\XUnit\xunit.console.clr4.exe
  $filesDir = "$baseDir\_build"
  $nugetDir = "$baseDir\_nuget"
  $packageDir = "$baseDir\_package"
  $mainVersion = "2.4.3"
}

# Aliases
task Default -depends Build
task Package -depends Clean-Solution,Clean-PackageFiles, Set-Version, Update-AssemblyInfoFiles, Build-Solution, Package-Files, Package-NuGet
task Clean -depends Clean-Solution
task TeamCity -depends  Clean-TeamCitySolution, Build-TeamCitySolution, Run-UnitTests, Run-IntegrationTests

# Build Tasks
task Build -depends  Clean-Solution, Build-Solution, Run-UnitTests, Run-IntegrationTests
task Build-2010 -depends  Clean-Solution-2010, Build-Solution-2010, Run-UnitTests, Run-IntegrationTests
task Build-2012 -depends  Clean-Solution-2012, Build-Solution-2012, Run-UnitTests, Run-IntegrationTests

task Set-Version {

	$version = git describe --abbrev=0 --tags
	$global:version = $version.substring(1) + '.' + (git log $($version + '..') --pretty=oneline | measure-object).Count
}

task Update-AssemblyInfoFiles {
	$commit = hg log --template '{rev}:{node}\n' -l 1
	Update-AssemblyInfoFiles $global:version $commit
}

task Run-Chutzpah -depends  Build-Solution {
  exec { & .\ConsoleRunner\bin\$configuration\chutzpah.console.exe ConsoleRunner\JS\test.html /file ConsoleRunner\JS\tests.js}
}

task Clean-PackageFiles {
    clean $nugetDir
    clean $filesDir
    clean $packageDir
}

# CodeBetter TeamCity does not have VS SDK installed so we use a custom solution that does not build the 
# VS components
task Clean-TeamCitySolution {
    exec { msbuild TeamCity.CodeBetter.sln /t:Clean /v:quiet }

}

task Clean-Solution -depends Clean-Solution-2010, Clean-Solution-2012

task Clean-Solution-2012 {
    exec { msbuild Chutzpah.VS2012.sln /t:Clean /v:quiet }
}

task Clean-Solution-2010 {
    exec { msbuild Chutzpah.VS2010.sln /t:Clean /v:quiet }
}

# CodeBetter TeamCity does not have VS SDK installed so we use a custom solution that does not build the 
# VS components
task Build-TeamCitySolution {
    exec { msbuild TeamCity.CodeBetter.sln /maxcpucount /t:Build /v:Minimal /p:Configuration=$configuration }
}

task Build-Solution -depends Build-Solution-2010, Build-Solution-2012

task Build-Solution-2012 {
    exec { msbuild Chutzpah.VS2012.sln /maxcpucount /t:Build /v:Minimal /p:Configuration=$configuration }
}

task Build-Solution-2010 {
    # Import environment variables for Visual Studio 2010
    if (test-path ("vsvars2010.ps1")) { 
      . ./vsvars2010.ps1 
    }
    
    exec { msbuild Chutzpah.VS2010.sln /maxcpucount /t:Build /v:Minimal /p:Configuration=$configuration }
}


task Run-PerfTester {
    $result = & "PerfTester\bin\$configuration\chutzpah.perftester.exe"
    Write-Output $result
    $result | Out-File "perf_results.txt" -Encoding ASCII
}

task Run-UnitTests {
    exec { & $xUnit "Facts\bin\$configuration\Facts.Chutzpah.dll" }
}

task Run-IntegrationTests {
    exec { & $xUnit "Facts.Integration\bin\$configuration\Facts.Integration.Chutzpah.dll" }
}

task Run-Phantom {
  $testFilePath = Resolve-Path $arg1;
  $type = $arg2;
  $mode = $arg3;
  if(-not $type){
    $type = "qunit";
  }
  $phantom = "3rdParty\Phantom\phantomjs.exe";
  $testFilePath = $testFilePath.Path.Replace("\","/");
  
  exec {  & $phantom "Chutzpah\JSRunners\$($type)Runner.js" "file:///$testFilePath" $mode }
}

task Package-Files -depends Clean-PackageFiles {
    
    create $filesDir, $packageDir
    copy-item "$baseDir\License.txt" -destination $filesDir
    copy-item "$baseDir\3rdParty\ServiceStack\LICENSE.BSD" -destination $filesDir\ServiceStack.LICENSE.BSD
    roboexec {robocopy "$baseDir\ConsoleRunner\bin\$configuration\" $filesDir /S /xd JS /xf *.xml}
    
    cd $filesDir
    exec { &"$baseDir\3rdParty\Zip\zip.exe" -r -9 "$packageDir\Chutzpah.$mainVersion.zip" *.* }
    cd $baseDir
    
    # Copy over Vsix Files
    copy-item "$baseDir\VisualStudio\bin\$configuration\chutzpah.visualstudio.vsix" -destination $packageDir
    copy-item "$baseDir\VS2012\bin\$configuration\Chutzpah.VS2012.vsix" -destination $packageDir
}

task Package-NuGet -depends Clean-PackageFiles {
    $nugetTools = "$nugetDir\tools"
    $nuspec = "$baseDir\Chutzpah.nuspec"
    
    create $nugetDir, $nugetTools, $packageDir
    
    copy-item "$baseDir\License.txt", $nuspec -destination $nugetDir
    copy-item "$baseDir\3rdParty\ServiceStack\LICENSE.BSD" -destination $nugetDir\ServiceStack.LICENSE.BSD
    roboexec {robocopy "$baseDir\ConsoleRunner\bin\$configuration\" $nugetTools /S /xd JS /xf *.xml}
    $v = new-object -TypeName System.Version -ArgumentList $global:version
    regex-replace "$nugetDir\Chutzpah.nuspec" '(?m)@Version@' $v.ToString(3)
    exec { .\Tools\nuget.exe pack "$nugetDir\Chutzpah.nuspec" -o $packageDir }
}

task Push-Nuget -depends Set-Version {
  $v = new-object -TypeName System.Version -ArgumentList $global:version
	exec { .\Tools\nuget.exe push $packageDir\Chutzpah.$($v.ToString(3)).nupkg }
}


# Help 
task ? -Description "Help information" {
	Write-Documentation
}

function create([string[]]$paths) {
  foreach ($path in $paths) {
    if(-not (Test-Path $path)) {
      new-item -path $path -type directory | out-null
    }
  }
}

function regex-replace($filePath, $find, $replacement) {
    $regex = [regex] $find
    $content = [System.IO.File]::ReadAllText($filePath)
    
    Assert $regex.IsMatch($content) "Unable to find the regex '$find' to update the file '$filePath'"
    
    [System.IO.File]::WriteAllText($filePath, $regex.Replace($content, $replacement))
}

function clean([string[]]$paths) {
	foreach ($path in $paths) {
		remove-item -force -recurse $path -ErrorAction SilentlyContinue
	}
}

function roboexec([scriptblock]$cmd) {
    & $cmd | out-null
    if ($lastexitcode -eq 0) { throw "No files were copied for command: " + $cmd }
}

# Borrowed from Luis Rocha's Blog (http://www.luisrocha.net/2009/11/setting-assembly-version-with-windows.html)
function Update-AssemblyInfoFiles ([string] $version, [string] $commit) {
    $assemblyVersionPattern = 'AssemblyVersion\("[0-9]+(\.([0-9]+|\*)){1,3}"\)'
    $fileVersionPattern = 'AssemblyFileVersion\("[0-9]+(\.([0-9]+|\*)){1,3}"\)'
    $fileCommitPattern = 'AssemblyTrademark\("[a-f0-9]*"\)'
    $assemblyVersion = 'AssemblyVersion("' + $version + '")';
    $fileVersion = 'AssemblyFileVersion("' + $version + '")';
    $commitVersion = 'AssemblyTrademark("' + $commit + '")';

    Get-ChildItem -path $baseDir -r -filter AssemblyInfo.cs | ForEach-Object {
        $filename = $_.Directory.ToString() + '\' + $_.Name
        $filename + ' -> ' + $version
        
        # If you are using a source control that requires to check-out files before 
        # modifying them, make sure to check-out the file here.
        # For example, TFS will require the following command:
        # tf checkout $filename
    
        (Get-Content $filename) | ForEach-Object {
            % {$_ -replace $assemblyVersionPattern, $assemblyVersion } |
            % {$_ -replace $fileVersionPattern, $fileVersion } |
            % {$_ -replace $fileCommitPattern, $commitVersion }
        } | Set-Content $filename
    }
}
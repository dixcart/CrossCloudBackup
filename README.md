
#CrossCloudBackup

An application to automatically backup Rackspace Cloud Files to AWS S3 and vice versa.

##Instructions

  * Compile and copy exe to either an EC2 instance or a Rackspace Cloud Server (for bandwidth cost reduction).
  * Run the application once to generate a blank config XML file.
  * Modify the XML config file with your keys (see config section below)
  * Run manually or schedule the app to run, it will sync all buckets and containers between the services

##Config

  * _AWSKey_: AWS IAM user Key
  * _AWSSecret_: AWS IAM user secret
  * _AWSRegion_: AWS region to use, needs one instance of app per region
  * _RSUsername_: RackSpace user name
  * _RSAPIKey_: RackSpace API Key
  * _RSUseServiceNet_: (true/false) Use internal Rackspace network, only if app is running on a Cloud Server
  * _RSLocation_: RackSpace API location (UK/US)
  * _ExcludeBuckets_: Comma separated list of S3 buckets to exclude
  * _ExcludeContainers_: Comma separated list of Cloud Files Containers to exclude
  * _RSBackupContainer_: Container to store your S3 files on Rackspace
  * _S3BackupBucket_: Bucket to store your Cloud Files on S3

##License

<a rel="license" href="http://creativecommons.org/licenses/by-sa/3.0/"><img alt="Creative Commons Licence" style="border-width:0" src="http://i.creativecommons.org/l/by-sa/3.0/88x31.png" /></a><br /><span xmlns:dct="http://purl.org/dc/terms/" href="http://purl.org/dc/dcmitype/InteractiveResource" property="dct:title" rel="dct:type">CrossCloudBackup</span> by <a xmlns:cc="http://creativecommons.org/ns#" href="http://dixcart.com/it" property="cc:attributionName" rel="cc:attributionURL">Dixcart Technical Solutions Limited</a> is licensed under a <a rel="license" href="http://creativecommons.org/licenses/by-sa/3.0/">Creative Commons Attribution-ShareAlike 3.0 Unported License</a>.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
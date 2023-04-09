import * as PDFJS from "pdfjs-dist/webpack";
import { Select } from 'mdb-ui-kit';
import * as d3 from "d3";
import * as $ from 'jquery';
import "bootstrap-multiselect";

//https://ralzohairi.medium.com/audio-recording-in-javascript-96eed45b75ee
//https://orangeable.com/javascript/equalizer-web-audio-api
//https://github.com/orangeable/javascript-equalizer/blob/master/js/main.js
//https://orangeable.com/javascript/equalizer-web-audio-api
//https://github.com/orangeable/javascript-equalizer
//https://openai.com/api/
//https://platform.openai.com/examples
//https://blog.teamtreehouse.com/getting-started-speech-synthesis-api#:~:text=To%20use%20a%20voice%2C%20set,speechSynthesis.
//https://developer.mozilla.org/en-US/docs/Web/API/SpeechSynthesis
//https://ourcodeworld.com/articles/read/405/how-to-convert-pdf-to-text-extract-text-from-pdf-with-javascript

import * as moment from 'moment';
import tinymce from "tinymce";

export default class app {

    private canvas: HTMLCanvasElement;
    private voiceRecognizer: VoiceRecognizer;
    private equalizer: Equalizer;

    constructor(private userFirstName: string, private profilePicture: string) {

        let welcomeMsg = `<div class="modal fade show" id="exampleModal" tabindex="-1" aria-labelledby="exampleModalLabel" aria-modal="true" role="dialog" style="display: block;">
                             <div class="modal-dialog modal-dialog-centered">
                                <div class="modal-content">
                                  <div class="modal-header">
                                    
                                  </div>
                                  <div class="modal-body">
                                    <img src="/img/Lucy.png" class="rounded" width="150" height="150" alt="" aria-controls="#picker-editor" style="position: absolute;right: -55px;top: -91px;border: 5px solid #fff;">
                                    <h2 class="modal-title text-primary" id="exampleModalLabel">Ask Lucy</h2>

                                  </div>
                                  <div class="modal-footer">
                                    <button type="button" class="btn btn-secondary" data-mdb-dismiss="modal">OK</button>
                                  </div>
                                </div>
                              </div>
                              </div>`;

        let myModalEl = $(welcomeMsg);

        myModalEl.on('hidden.bs.modal', (event) => {
            // do something...
            this.initUi();
        });

        myModalEl.modal('toggle');

        $('input[type="file"]').on('change', (event) => {
            event.preventDefault();
            let filepath = URL.createObjectURL((event.target as HTMLInputElement).files[0]);
            this.parsePdf(filepath).then((textPage: string) => {
                this.addToChatBox(textPage);
            });
        });

        //tinymce.init({
        //    selector: "[data-emojiable='true']",
        //    plugins: "emoticons autoresize",
        //    toolbar: "emoticons",
        //    toolbar_location: "bottom",
        //    menubar: false,
        //    statusbar: false
        //});
    }

    private initUi() {

        //// create an array of options
        //const options = [
        //    { value: 'apple', label: 'Apple' },
        //    { value: 'banana', label: 'Banana' },
        //    { value: 'orange', label: 'Orange' },
        //];

        //// create a select element using mdb.Select component
        //const selectElement = new Select(document.getElementById('mySelect'), {
        //    options: options,
        //    clearable: true,
        //    search: true,
        //    placeholder: 'Select a fruit',
        //});

        this.voiceRecognizer = new VoiceRecognizer(this.userFirstName, this.profilePicture);
        this.equalizer = new Equalizer(this.profilePicture);

        $('#button-send-message').on('click', (event) => {
            event.preventDefault();

            let msg = $('#textArea-message').val().toString();
            //let msg = tinymce.activeEditor.getContent();

            this.addToChatWindow(msg, this.userFirstName).then(() => {

                let diagnostic = document.getElementsByClassName('list-unstyled custom-scrollbar').item(0) as HTMLElement;
                let lastMsg = document.getElementsByClassName('direct-chat-msg');

                diagnostic.scrollTo({ top: (lastMsg.item(lastMsg.length - 1) as HTMLElement).offsetTop, behavior: 'smooth' });

                $('#textArea-message').val('');
                //tinymce.activeEditor.setContent('');

                if (msg.toLowerCase().includes('draw') || msg.toLowerCase().includes('paint') || msg.toLowerCase().includes('sketch') || msg.toLowerCase().includes('portray') || msg.toLowerCase().includes('plot')) {
                    this.voiceRecognizer.Draw(this.voiceRecognizer.diagnostic, msg);
                } else {
                    this.voiceRecognizer.Chat(this.voiceRecognizer.diagnostic, msg, this.voiceRecognizer.recognition);
                }

            });
        });

        $("#mute").on('click', (event) => {
            event.preventDefault();

            $(event.currentTarget).toggleClass('btn-info btn-primary')
            $(event.currentTarget).find('.fas').toggleClass("fa-microphone-alt fa-microphone-alt-slash");

            if ($(event.currentTarget).find('.fas').hasClass('fa-microphone-alt')) {
                this.voiceRecognizer.start();
                $('.form-check-label').text('Audio chat is enabled.');
            } else {
                this.voiceRecognizer.stop();
                $('.form-check-label').text('Audio chat is not enabled.');
            }
        });

        $('#flexSwitchCheckChecked').on('click', (event) => {
            //event.preventDefault();
            if ($(event.currentTarget).is(':checked')) {
                this.voiceRecognizer.start();
                $('.form-check-label').text('Audio chat is enabled.');
            } else {
                this.voiceRecognizer.stop();
                $('.form-check-label').text('Audio chat is not enabled.');
            }
        });
    }

    private addToChatWindow(textPage: string, userFirstName: string) {

        return new Promise((resolve, reject) => {
            try {
                let li: HTMLLIElement = document.createElement('li');
                li.classList.add(...['d-flex', 'justify-content-between', 'mb-4', 'direct-chat-msg']);
                li.innerHTML = `<img src="${this.profilePicture}" alt="avatar"
                                                     class="rounded-circle d-flex align-self-start me-3 shadow-1-strong" width="60">
                                                <div class="card w-100">
                                                    <div class="card-header d-flex justify-content-between p-3">
                                                        <p class="fw-bold mb-0">${userFirstName}</p>
                                                        <p class="text-muted small mb-0"><i class="far fa-clock"></i> ${moment().format("D MMM h:mm a")}</p>
                                                    </div>
                                                    <div class="card-body">
                                                        <p class="mb-0">
                                                            ${textPage}
                                                        </p>
                                                    </div>
                                                </div>`;
                let msg_li = document.getElementsByClassName('list-unstyled custom-scrollbar').item(0).appendChild(li);
                resolve(msg_li);
            } catch (e) {
                reject();
            }
        });
    }

    private addToChatBox(textPage: string) {

        $('#textArea-message').val(textPage).trigger('focus');
        //tinymce.activeEditor.setContent(`<p>${textPage}</p>`);
    }


    private parsePdf(filepath: string) {

        return new Promise((resolve, reject) => {

            try {
                PDFJS.getDocument(filepath).promise.then((PDFDocumentInstance) => {

                    // Use the PDFDocumentInstance To extract the text later
                    const totalPages = PDFDocumentInstance.numPages;
                    const pageNumber = 1;

                    // Extract the text
                    this.getPageText(pageNumber, PDFDocumentInstance).then((textPage: string) => {
                        // Show the text of the page in the console
                        resolve(textPage);
                    });
                }, (reason) => {
                    // PDF loading error
                    reject(reason);
                });

            } catch (e) {
                reject(e);
            }
        });
    }

    /**
     * Retrieves the text of a specif page within a PDF Document obtained through pdf.js 
     * 
     * @param {Integer} pageNum Specifies the number of the page 
     * @param {PDFDocument} PDFDocumentInstance The PDF document obtained 
     **/
    private getPageText(pageNum, PDFDocumentInstance) {
        // Return a Promise that is solved once the text of the page is retrieven
        return new Promise((resolve, reject) => {
            PDFDocumentInstance.getPage(pageNum).then((pdfPage) => {
                // The main trick to obtain the text of the PDF page, use the getTextContent method
                pdfPage.getTextContent().then((textContent) => {
                    const textItems = textContent.items;
                    let finalString = "";

                    // Concatenate the string of the item to the final string
                    for (let i = 0; i < textItems.length; i++) {
                        let item = textItems[i];

                        finalString += item.str + " ";

                        let percent_loaded = Math.ceil((i / (textItems.length - 1)) * 100);
                        document.getElementById('progress-pdf-parser').style.width = `${percent_loaded}%`;
                        document.getElementById('progress-pdf-parser').setAttribute('aria-valuenow', percent_loaded.toFixed(2));
                        console.log(percent_loaded);
                    }

                    // Solve promise with the text retrieven from the page
                    resolve(finalString);
                });
            });
        });
    }

}

class VoiceRecognizer {

    private grammar: string;
    public diagnostic: HTMLElement;
    public recognition:SpeechRecognition;
    private speechRecognitionList: SpeechGrammarList;
    private voice: SpeechSynthesisVoice;
    private conversation: any[];
    private language: string = "en-GB";

    constructor(private userFirstName: string, private profilePicture: string) {

        this.grammar = '#JSGF V1.0; grammar colors; public <color> = aqua | azure | beige | bisque | black | blue | brown | chocolate | coral | crimson | cyan | fuchsia | ghostwhite | gold | goldenrod | gray | green | indigo | ivory | khaki | lavender | lime | linen | magenta | maroon | moccasin | navy | olive | orange | orchid | peru | pink | plum | purple | red | salmon | sienna | silver | snow | tan | teal | thistle | tomato | turquoise | violet | white | yellow ;'

        this.diagnostic = document.getElementsByClassName('list-unstyled custom-scrollbar').item(0) as HTMLElement;
        this.recognition = new webkitSpeechRecognition() || new SpeechRecognition();
        this.speechRecognitionList = new webkitSpeechGrammarList() || new SpeechGrammarList();

        this.speechRecognitionList.addFromString(this.grammar, 1);
        this.recognition.grammars = this.speechRecognitionList;
        this.recognition.continuous = true;
        this.recognition.lang = this.language;
        this.recognition.interimResults = false;
        this.recognition.maxAlternatives = 1;

        const synth = speechSynthesis;
        let voices: SpeechSynthesisVoice[] = synth.getVoices();

        speechSynthesis.onvoiceschanged = () => {

            voices = speechSynthesis.getVoices();
            console.log(...voices);
            let langs: string[] = Array.from(new Set(voices.map((voice) => { return voice.lang })));
            langs.sort();

            $('#select-languages').val('').multiselect({
                nonSelectedText: 'Please select language',
                disableIfEmpty: true,
                buttonClass: 'btn btn-success',
                buttonWidth: '100%',
                maxHeight: 450,
                selectedClass: 'active multiselect-selected',
                includeSelectAllOption: false,
                buttonContainer: '<div class="multiselect-buttons btn-group d-flex w-100"></div>',
                templates: {
                    button: `<button type="button" class="multiselect dropdown-bordered dropdown-toggle dropdown-toggle-split" data-mdb-toggle="dropdown">
                                <span class="multiselect-selected-text"> </span>
                             </button>`,
                    ul: '<ul class="multiselect-container dropdown-menu custom-scrollbar" style="min-width:175px;"></ul>',
                    li: `<li>
                            <a class="dropdown-item">
                                <label class="radio">
                                <input class="preview-subject ellipsis font-weight-medium text-dark"></label>
                            </a>
                         </li>`
                },
                onChange: (option, checked) => {

                    this.language = option.html();
                    this.recognition.lang = this.language;

                    if (this.language === 'en-GB') {
                        //Microsoft Libby Online (Natural) - English (United Kingdom)
                        //Microsoft Salma Online (Natural) - Arabic (Egypt)

                        this.voice = voices.filter((voice) => { return voice.lang.includes(this.language) && voice.name.includes('Libby'); })[0];
                        console.log(this.voice.name);
                    } else if (this.language === 'ar-EG') {
                        this.voice = voices.filter((voice) => { return voice.lang.includes(this.language) && voice.name.includes('Salma'); })[0];
                        console.log(this.voice.name);
                    } else if (this.language === 'es-ES') {
                        this.voice = voices.filter((voice) => { return voice.lang.includes(this.language) && voice.name.includes('Elvira'); })[0];
                        console.log(this.voice.name);
                    } else if (this.language === 'hi-IN') {
                        this.voice = voices.filter((voice) => { return voice.lang.includes(this.language) && voice.name.includes('Swara'); })[0];
                        console.log(this.voice.name);
                    } else if (this.language === 'it-IT') {
                        this.voice = voices.filter((voice) => { return voice.lang.includes(this.language) && voice.name.includes('Elsa'); })[0];
                        console.log(this.voice.name);
                    } else {
                        this.voice = voices.filter((voice) => { return voice.lang.includes(this.language); })[0];
                        console.log(this.voice.name);
                    }

                }
            });

            let options: any[] = [];

            langs.forEach((lang, index) => {
                options.push({ label: lang, title: lang, value: index, selected: lang === this.language });
            });

            $('#select-languages').multiselect('dataprovider', options);
            $('#select-languages').multiselect('rebuild');

            console.log(voices);

            if (!this.voice) {

                console.log($('#select-languages option:selected').text());

                this.voice = voices.filter((voice) => { return voice.name.toLowerCase().includes('female'); })[0];
            }
        };

        this.recognition.onresult = (event) => {

            let results = event.results;
            const msg = results.item(results.length - 1)[0].transcript;
            this.diagnostic.innerHTML += `<li class="d-flex justify-content-between mb-4 direct-chat-msg" dir="auto">
                                                <img src="${profilePicture}" alt="avatar"
                                                     class="rounded-circle d-flex align-self-start me-3 shadow-1-strong" width="60">
                                                <div class="card w-100">
                                                    <div class="card-header d-flex justify-content-between p-3">
                                                        <p class="fw-bold mb-0">${userFirstName}</p>
                                                        <p class="text-muted small mb-0"><i class="far fa-clock"></i> ${moment().format("D MMM h:mm a")}</p>
                                                    </div>
                                                    <div class="card-body">
                                                        <p class="mb-0">
                                                            ${msg}
                                                        </p>
                                                    </div>
                                                </div>
                                            </li>`;

            let lastMsg = document.getElementsByClassName('direct-chat-msg');

            this.diagnostic.scrollTo({ top: (lastMsg.item(lastMsg.length - 1) as HTMLElement).offsetTop, behavior: 'smooth' });

            if (msg.toLowerCase().includes('draw') || msg.toLowerCase().includes('paint') || msg.toLowerCase().includes('sketch') || msg.toLowerCase().includes('portray') || msg.toLowerCase().includes('plot')) {
                this.Draw(this.diagnostic, msg);
            } else {
                this.Chat(this.diagnostic, `${msg}\n`, this.recognition);
            }
        }

        if ($('#flexSwitchCheckChecked').is(':checked')) {
            this.recognition.start();
        } else {
            this.recognition.stop();
        }

        this.conversation = [{ "role": "user", "content": `Good Morning, my name is ${userFirstName}.` },
            { "role": "assistant", "content": `Good morning ${userFirstName}, How may I assest you today?` },
        {
            "role": "user", "content": "What is your name?"
        },
        { "role": "assistant", "content": "My Name is Lucy." }, {
            "role": "user", "content": "Hello Lucy."
        },
            { "role": "assistant", "content": `Hello ${userFirstName}.` }];
    }

    start() {
        this.recognition.start();
    }

    stop() {
        this.recognition.stop();
    }

    Chat(diagnostic: HTMLElement, prompt: string, recognition: SpeechRecognition) {

        if (prompt && prompt !== '') {
            this.conversation.push({ "role": "user", "content": prompt });
        }

        return $.ajax({
            type: 'POST',
            url: 'https://api.openai.com/v1/chat/completions',
            contentType: "application/json",
            beforeSend: function (xhr) {
                xhr.setRequestHeader("Authorization", "Bearer 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");
            },
            data: JSON.stringify({
                model: "gpt-3.5-turbo",
                messages: this.conversation
            })
        }).then((response, textStatus, xhr) => {
            if (xhr.status === 200) {
                console.log(JSON.stringify(response));
                let msg = response.choices[0].message.content;

                this.conversation.push({ "role": "assistant", "content": msg });

                diagnostic.innerHTML += `<li class="d-flex justify-content-between mb-4 direct-chat-msg pull-right" dir="auto">
                                                <div class="card w-100">
                                                    <div class="card-header d-flex justify-content-between p-3">
                                                        <p class="fw-bold mb-0">Lucy</p>
                                                        <p class="text-muted small mb-0"><i class="far fa-clock"></i> ${moment().format("D MMM h:mm a")}</p>
                                                    </div>
                                                    <div class="card-body">
                                                        <p class="mb-0">
                                                             ${msg}
                                                        </p>
                                                    </div>
                                                </div>
                                                <img src="/img/Lucy.png" alt="avatar"
                                                     class="rounded-circle d-flex align-self-start ms-3 shadow-1-strong" width="60">
                                            </li>`;

                let lastMsg = document.getElementsByClassName('direct-chat-msg');

                this.diagnostic.scrollTo({ top: (lastMsg.item(lastMsg.length - 1) as HTMLElement).offsetTop, behavior: 'smooth' });

                let utterance = new SpeechSynthesisUtterance(msg);
                utterance.lang = this.language;
                utterance.voice = this.voice;
                utterance.rate = 1;
                utterance.pitch = 1;
                utterance.volume = 0.5;

                utterance.onend = (event) => {
                    try {
                        if ($('#flexSwitchCheckChecked').is(':checked')) {
                            this.recognition.start();
                        } else {
                            this.recognition.stop();
                        }
                    } catch (e) {
                        console.log(e);
                    }
                };

                utterance.onstart = (event) => {

                    console.log(event.currentTarget);

                    navigator.mediaDevices.enumerateDevices()
                        // set `getUserMedia()` constraints to "auidooutput", where avaialable
                        // see https://bugzilla.mozilla.org/show_bug.cgi?id=934425, https://stackoverflow.com/q/33761770

                        .then(devices => {
                            let audiooutput = devices.find(device => device.kind === "audiooutput" && device.deviceId === "default");
                            let label = audiooutput.label.replace('Default - ', '');

                            audiooutput = devices.find(device => device.kind === "audiooutput" && device.label === label);

                            if (audiooutput) {
                                const constraints: MediaStreamConstraints = {
                                    audio: {
                                        deviceId: { exact: audiooutput.deviceId }
                                    }
                                };

                                navigator.mediaDevices.getUserMedia(constraints).then((stream: MediaStream) => {

                                    console.log(stream.getAudioTracks()[0].label);

                                    let equalizer = new Equalizer(this.profilePicture, stream);

                                    console.log('stream.active: ', stream.getAudioTracks().length);
                                }).catch(error => console.error( error));
                            }
                        });
                };

                speechSynthesis.speak(utterance);


            }
        }).fail((XMLHttpRequest, textStatus, errorThrown) => {
            console.log("Message: " + JSON.stringify(XMLHttpRequest));
            console.log("Status: " + textStatus);
            console.log("Error: " + errorThrown);
        });
    }

    Draw(diagnostic: HTMLElement, prompt: string) {

        return $.ajax({
            type: 'POST',
            url: 'https://api.openai.com/v1/images/generations',
            contentType: "application/json",
            beforeSend: function (xhr) {
                xhr.setRequestHeader("Authorization", "Bearer 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");
            },
            data: JSON.stringify({
                prompt: prompt,
                n: 1,
                size: "1024x1024"
            })
        }).then((response, textStatus, xhr) => {
            if (xhr.status === 200) {
                console.log(JSON.stringify(response));

                diagnostic.innerHTML += `<li class="d-flex justify-content-between mb-4 direct-chat-msg pull-right">
                                                <div class="card w-100">
                                                    <div class="card-header d-flex justify-content-between p-3">
                                                        <p class="fw-bold mb-0">Lucy</p>
                                                        <p class="text-muted small mb-0"><i class="far fa-clock"></i> ${moment().format("D MMM h:mm a")}</p>
                                                    </div>
                                                    <div class="card-body">
                                                        <div class="canvas-imagine" style="display: block; min-height: 250px;">
                                                        </div>
                                                    </div>
                                                </div>
                                                <img src="/img/Lucy.png" alt="avatar"
                                                     class="rounded-circle d-flex align-self-start ms-3 shadow-1-strong" width="60">
                                            </li>`;

                let canvases = document.getElementsByClassName('canvas-imagine');

                let canvas = (canvases.item(canvases.length - 1) as HTMLElement);
                canvas.style.background = `url(${response.data[0].url})`;
                canvas.style.backgroundSize = 'contain';
                canvas.style.backgroundRepeat = 'no-repeat';
                canvas.style.backgroundPosition = 'center';

                let lastMsg = document.getElementsByClassName('direct-chat-msg');

                this.diagnostic.scrollTo({ top: (lastMsg.item(lastMsg.length - 1) as HTMLElement).offsetTop, behavior: 'smooth' });
            }
        }).fail((XMLHttpRequest, textStatus, errorThrown) => {
            console.log("Message: " + JSON.stringify(XMLHttpRequest));
            console.log("Status: " + textStatus);
            console.log("Error: " + errorThrown);
        });
    }

}

class Equalizer {

    constructor(private profilePicture: string, stream?: MediaStream) {
        //$(document).on('click', 'img', () => {

        // Set up forked web audio context, for multiple browsers
        // window. is needed otherwise Safari explodes
        const audioCtx: AudioContext = new AudioContext();

        // Set up the different audio nodes we will use for the app
        const analyser: AnalyserNode = audioCtx.createAnalyser();
        const distortion: WaveShaperNode = audioCtx.createWaveShaper();
        const gainNode: GainNode = audioCtx.createGain();
        const biquadFilter: BiquadFilterNode = audioCtx.createBiquadFilter();
        const convolver: ConvolverNode = audioCtx.createConvolver();
        const echoDelay = this.createEchoDelayEffect(audioCtx);

        analyser.minDecibels = -90;
        analyser.maxDecibels = -10;
        analyser.smoothingTimeConstant = 0.85;

        if (stream === null || typeof stream === 'undefined') {
            // Main block for doing the audio recording
            if (navigator.mediaDevices.getUserMedia) {
                console.log("getUserMedia supported.");
                const constraints: MediaStreamConstraints = { audio: true };

                this.initMediaDevices(constraints)
                    .then((stream: MediaStream) => {
                        let source;
                        let tracks = stream.getAudioTracks();
                        console.log('tracks.length: ', tracks.length);

                        source = audioCtx.createMediaStreamSource(stream);
                        source.connect(gainNode);
                        gainNode.connect(analyser);
                        //analyser.connect(audioCtx.destination);
                        this.visualizeD3(analyser);
                    })
                    .catch(function (err) {
                        console.log("The following gUM error occured: " + err);
                    });
            } else {
                console.log("getUserMedia not supported on your browser!");
            }
        } else {
            let source: MediaStreamAudioSourceNode;

            console.log(stream.getAudioTracks().length + ', ' + JSON.stringify(stream.getAudioTracks()[0].getConstraints().deviceId) + ', ' + stream.getAudioTracks()[0].kind + stream.getAudioTracks()[0].label + ', ' + stream.id);
            source = audioCtx.createMediaStreamSource(stream.clone());
            source.connect(gainNode);
            gainNode.connect(analyser);

            this.visualizeD3(analyser);
        }
    }

    createEchoDelayEffect(audioContext) {
        const delay = audioContext.createDelay(1);
        const dryNode = audioContext.createGain();
        const wetNode = audioContext.createGain();
        const mixer = audioContext.createGain();
        const filter = audioContext.createBiquadFilter();

        delay.delayTime.value = 0.75;
        dryNode.gain.value = 1;
        wetNode.gain.value = 0;
        filter.frequency.value = 1100;
        filter.type = "highpass";

        return {
            apply: function () {
                wetNode.gain.setValueAtTime(0.75, audioContext.currentTime);
            },
            discard: function () {
                wetNode.gain.setValueAtTime(0, audioContext.currentTime);
            },
            isApplied: function () {
                return wetNode.gain.value > 0;
            },
            placeBetween: function (inputNode, outputNode) {
                inputNode.connect(delay);
                delay.connect(wetNode);
                wetNode.connect(filter);
                filter.connect(delay);

                inputNode.connect(dryNode);
                dryNode.connect(mixer);
                wetNode.connect(mixer);
                mixer.connect(outputNode);
            }
        };
    }

    visualize(analyser) {

        // Set up canvas context for visualizer
        const canvas: HTMLCanvasElement = document.querySelector(".visualizer");
        const canvasCtx = canvas.getContext("2d");

        const intendedWidth = document.querySelector(".custom-5").clientWidth.toString();
        canvas.setAttribute("width", intendedWidth);
        let drawVisual;

        let WIDTH = canvas.width;
        let HEIGHT = canvas.height;

        let visualSetting = "sinewave";
        console.log(visualSetting);

        if (visualSetting === "sinewave") {
            analyser.fftSize = 2048;
            const bufferLength = analyser.fftSize;
            console.log(bufferLength);

            // We can use Float32Array instead of Uint8Array if we want higher precision
            // const dataArray = new Float32Array(bufferLength);
            const dataArray = new Uint8Array(bufferLength);

            canvasCtx.clearRect(0, 0, WIDTH, HEIGHT);

            const render = function () {
                drawVisual = requestAnimationFrame(render);

                analyser.getByteTimeDomainData(dataArray);

                canvasCtx.fillStyle = "rgba(255, 255, 255, 1)";
                canvasCtx.fillRect(0, 0, WIDTH, HEIGHT);

                canvasCtx.lineWidth = 2;
                canvasCtx.strokeStyle = "rgb(163, 23, 253)";

                canvasCtx.beginPath();

                const sliceWidth = (WIDTH * 1.0) / bufferLength;
                let x = 0;

                for (let i = 0; i < bufferLength; i++) {
                    let v = dataArray[i] / 128.0;
                    let y = (v * HEIGHT) / 2;

                    if (i === 0) {
                        canvasCtx.moveTo(x, y);
                    } else {
                        canvasCtx.lineTo(x, y);
                    }

                    x += sliceWidth;
                }

                canvasCtx.lineTo(canvas.width, canvas.height / 2);
                canvasCtx.stroke();
            };

            render();
        } else if (visualSetting == "frequencybars") {
            analyser.fftSize = 256;
            const bufferLengthAlt = analyser.frequencyBinCount;
            console.log(bufferLengthAlt);

            // See comment above for Float32Array()
            const dataArrayAlt = new Uint8Array(bufferLengthAlt);

            canvasCtx.clearRect(0, 0, WIDTH, HEIGHT);

            const drawAlt = function () {
                drawVisual = requestAnimationFrame(drawAlt);

                analyser.getByteFrequencyData(dataArrayAlt);

                canvasCtx.fillStyle = "rgba(34, 45, 50, 1)";
                canvasCtx.fillRect(0, 0, WIDTH, HEIGHT);

                const barWidth = (WIDTH / bufferLengthAlt) * 2.5;
                let barHeight;
                let x = 0;

                for (let i = 0; i < bufferLengthAlt; i++) {
                    barHeight = dataArrayAlt[i];

                    canvasCtx.fillStyle = "rgb(" + (barHeight + 100) + ",50,50)";
                    canvasCtx.fillRect(
                        x,
                        HEIGHT - barHeight / 2,
                        barWidth,
                        barHeight / 2
                    );

                    x += barWidth + 1;
                }
            };

            drawAlt();
        } else if (visualSetting == "off") {
            canvasCtx.clearRect(0, 0, WIDTH, HEIGHT);
            canvasCtx.fillStyle = "red";
            canvasCtx.fillRect(0, 0, WIDTH, HEIGHT);
        }
    }

    visualizeD3(analyser: AnalyserNode) {

        let visualSetting = "sinewave";
        console.log(visualSetting);

        if (visualSetting === "sinewave") {
            analyser.fftSize = 2048;
            const bufferLength = analyser.fftSize;
            //console.log(bufferLength);

            // We can use Float32Array instead of Uint8Array if we want higher precision
            //const dataArray = new Float32Array(bufferLength);
            //const bufferLength = analyser.frequencyBinCount;
            const dataArray = new Uint8Array(bufferLength);

            console.log(this.profilePicture);

            const render = () => {

                let drawVisual = requestAnimationFrame(render);

                //analyser.getByteFrequencyData(dataArray);
                analyser.getByteFrequencyData(dataArray);

                //console.log(dataArray.length);
                const svg = d3.select('.svg-visualizer');
                svg.attr('background-color', 'white');
                svg.selectAll('*').remove();
                const margin = {
                    top: 0,
                    right: 0,
                    bottom: 0,
                    left: 0
                };
                const width = +svg.attr('width') - margin.left - margin.right;
                const height = +svg.attr('height') - margin.top - margin.bottom;

                // content area of your visualization
                const vis = svg.append('g')
                    .attr('transform', `translate(${margin.left + width / 2},${margin.top + height / 2})`);

                // show scales
                const xScale = d3.scaleLinear()
                    .domain([-128, 128])
                    .range([-width / 2, width / 2]);

                // draw circle
                const radius = 115;

                const length = 256//64;
                const amplitude = 5;

                const radialGenerator = d3.lineRadial()
                    .angle(d => d.angle)
                    .radius(d => d.radius)
                    .curve(d3.curveCardinalClosed)

                const radialScale = d3.scaleLinear()
                    .domain([0, length])
                    .range([0, Math.PI * 2]);

                const data = d3.range(length).map((d, i)=> {
                    return {
                        angle: radialScale(d),
                        radius: xScale(radius) + (dataArray[i] / 128.0) * amplitude
                    }
                });

                const wave = vis.append('path')
                    .attr('d', radialGenerator(data))
                    .attr('fill', '#ffffff')
                    .attr('stroke', '#9575CD')
                    .attr('stroke-width', '2px');

                const defs = svg.append("defs").attr("id", "imgdefs")

                const catpattern = defs.append("pattern")
                    .attr("id", "catpattern")
                    .attr("height", 1)
                    .attr("width", 1)
                    .attr("x", "0")
                    .attr("y", "0");

                catpattern.append("image")
                    .attr("height", 150)
                    .attr("width", 150)
                    .attr("xlink:href", () => { return this.profilePicture; })
                    .attr("preserveAspectRatio","none");

                vis.append("circle")
                    .attr("r", 75)
                    .attr("cy", 0)
                    .attr("cx", 0)
                    .attr('stroke', '#9575CD')
                    .attr('stroke-width', '3px')
                    .attr("fill", "url(#catpattern)");
                    
            };

            render();
        }
    }

    initMediaDevices(constraints: MediaStreamConstraints) {

        return new Promise(function (resolve, reject) {

            if (!navigator.mediaDevices.getUserMedia || navigator.mediaDevices === undefined || navigator.mediaDevices.getUserMedia === undefined) {

                reject(
                    new Error("getUserMedia is not implemented in this browser")
                );
            } else {

                // Otherwise, wrap the call to the old navigator.getUserMedia with a Promise

                resolve(navigator.mediaDevices.getUserMedia(constraints));
            }
        });
    }

    voiceChange(distortion, biquadFilter, audioCtx, echoDelay, gainNode, convolver) {
        distortion.oversample = "4x";
        biquadFilter.gain.setTargetAtTime(0, audioCtx.currentTime, 0);

        let voiceSetting = "off";
        console.log(voiceSetting);

        if (echoDelay.isApplied()) {
            echoDelay.discard();
        }

        // When convolver is selected it is connected back into the audio path
        if (voiceSetting == "convolver") {
            biquadFilter.disconnect(0);
            biquadFilter.connect(convolver);
        } else {
            biquadFilter.disconnect(0);
            biquadFilter.connect(gainNode);

            if (voiceSetting == "distortion") {
                distortion.curve = this.makeDistortionCurve(400);
            } else if (voiceSetting == "biquad") {
                biquadFilter.type = "lowshelf";
                biquadFilter.frequency.setTargetAtTime(1000, audioCtx.currentTime, 0);
                biquadFilter.gain.setTargetAtTime(25, audioCtx.currentTime, 0);
            } else if (voiceSetting == "delay") {
                echoDelay.apply();
            } else if (voiceSetting == "off") {
                console.log("Voice settings turned off");
            }
        }
    }

    // Distortion curve for the waveshaper, thanks to Kevin Ennis
    // http://stackoverflow.com/questions/22312841/waveshaper-node-in-webaudio-how-to-emulate-distortion
    makeDistortionCurve(amount) {
        let k = typeof amount === "number" ? amount : 50,
            n_samples = 44100,
            curve = new Float32Array(n_samples),
            deg = Math.PI / 180,
            i = 0,
            x;
        for (; i < n_samples; ++i) {
            x = (i * 2) / n_samples - 1;
            curve[i] = ((3 + k) * x * 20 * deg) / (Math.PI + k * Math.abs(x));
        }
        return curve;
    }
}

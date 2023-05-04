import * as PDFJS from "pdfjs-dist/webpack";
import { Select } from 'mdb-ui-kit';
import * as d3 from "d3";
import * as $ from 'jquery';
import "bootstrap-multiselect";

import { EventEmitter } from "events";

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
//https://medium.com/@david.richards.tech/ai-audio-conversations-with-openai-whisper-3c730a9c7123

import * as moment from 'moment';
import tinymce from "tinymce";
import Tags from "bootstrap5-tags";
import VizWaveform from "./visualisations/frequency";
import VizFrequency from "./visualisations/frequency";
import RecordVoiceOverIcon from '@mui/icons-material/RecordVoiceOver';
import ErrorManager from "./core/error-manager";

export default class app {

    private canvas: HTMLCanvasElement;
    private voiceRecognizer: VoiceRecognizer;
    private equalizer: Equalizer;
    private errMngr: ErrorManager;

    constructor(private userFirstName: string, private profilePicture: string) {

        this.errMngr = new ErrorManager();

        let welcomeMsg = `<div class="modal fade show" id="exampleModal" tabindex="-1" aria-labelledby="exampleModalLabel" aria-modal="true" role="dialog" style="display: block;" data-mdb-backdrop="static" data-mdb-keyboard="false">
                             <div class="modal-dialog modal-dialog-centered">
                                <div class="modal-content">
                                  <div class="modal-header border-0">
                                    <h3 class="display-6 pt-3 ps-3">Welcome ${userFirstName}</h3>
                                    <img src="/img/Lucy.png" class="rounded-circle shadow-1-strong" width="85" height="85" alt="" aria-controls="#picker-editor" >
                                  </div>
                                  <div class="modal-body border-0">
                                      <div class="d-flex justify-content-end align-items-end">
                                       <img src="/img/edge-logo.webp" class="rounded me-1" width="100" height="100" alt="" aria-controls="#picker-editor">
                                       <p class="lead">For better experience, we recommend you to use Microsoft Edge.</p>
                                      </div>
                                  </div>
                                  <div class="modal-footer border-0">
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

            let file: File = (event.target as HTMLInputElement).files[0];
            $('#span-file-info').text('Type: ' + file.type + ', Size: ' + (file.size / 1024) + ' KB');

            let filepath = URL.createObjectURL(file);

            // Todo: complete the MIME list
            //https://developer.mozilla.org/en-US/docs/Web/HTTP/Basics_of_HTTP/MIME_types/Common_types
            switch (file.type) {
                case 'application/pdf':
                    this.parsePdf(filepath).then((textPage: string) => {
                        this.addToChatBox(textPage);
                    });
                    break;
                case 'audio/mpeg':
                case 'audio/ogg':
                case 'audio/aac':
                case 'audio/opus':
                case 'audio/wav':
                case 'audio/webm':
                case 'audio/3gpp':
                case 'audio/3gpp2':
                case 'audio/x-m4a':
                    this.transcript(file).then((textPage: string) => {
                        this.addToChatBox(textPage);
                        this.addToAttachments(file).then((data: any) => {

                            $('#ul-chat-attachments').html(`<li class="list-group-item p-4">
                                                <div class="d-flex justify-content-between align-items-center">
                                                    <div class="fw-bold">${data.filename}</div>
                                                    <span class="badge rounded-pill badge-success">${moment.utc(moment.duration(data.audioduration, "seconds").asMilliseconds()).format("HH:mm:ss")}</span>
                                                </div>

                                                <div class="text-muted">
                                                    <audio id="audio-data" preload="auto">
                                                        <source src="${data.audiosrc}">
                                                    </audio>
                                                    <div id="audioplayer d-flex justify-content-between align-items-center">
                                                        <i id="pButton" class="fas fa-play"></i>
                                                        <div id="timeline">
                                                            <div id="playhead"></div>
                                                        </div>
                                                    </div>
                                                </div>
                                            </li>`);

                            $('[data-mdb-target="#modal-attachments"]').removeClass('d-none');
                        });
                    });
                    break;

                default:
            }

        });

        $(document).on('click', '#btn-upload-app', (event) => {

            event.preventDefault();

            let files = $('#fil-upload-app');
        });

        $(document).on('click', '#pButton', (event) => {

            event.preventDefault();

            let audio: HTMLAudioElement = $('#audio-data').get(0) as HTMLAudioElement;

            audio.addEventListener('timeupdate', (event) => {

                event.preventDefault();

                let audio = event.currentTarget as HTMLAudioElement;
                let position = audio.currentTime / audio.duration;
                let offset = Math.ceil($('#timeline').width() * position);

                $('#playhead').css('transform', `translate(${offset}px, 0)`);
            });

            audio.addEventListener('ended', (event) => {

                event.preventDefault();

                $('#pButton').toggleClass("fa-play fa-pause");

                $('#playhead').css('transform', `translate(0, 0)`);
            });

            if (($(event.currentTarget).get(0) as HTMLElement).classList.contains('fa-play')) {
                audio.play();
            } else {
                audio.pause();
            }

            $('#pButton').toggleClass("fa-play fa-pause");

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

        $('#textArea-chat-message').val('').trigger('focus');

        this.voiceRecognizer = new VoiceRecognizer(this.userFirstName, this.profilePicture);
        this.equalizer = new Equalizer(this.profilePicture);

        $('#button-send-message').on('click', (event) => {
            event.preventDefault();

            let msg = $('#textArea-chat-message').val().toString();

            if (msg && msg.length > 0) {

                this.addToChatWindow(msg, this.userFirstName).then(() => {

                    let diagnostic = document.getElementsByClassName('list-unstyled custom-scrollbar').item(0) as HTMLElement;
                    let lastMsg = document.getElementsByClassName('direct-chat-msg');

                    diagnostic.scrollTo({ top: (lastMsg.item(lastMsg.length - 1) as HTMLElement).offsetTop, behavior: 'smooth' });

                    $('#textArea-chat-message').val('');
                    $('#ul-chat-attachments').html('');
                    $('[data-mdb-target="#modal-attachments"]').addClass('d-none');

                    //tinymce.activeEditor.setContent('');

                    if (msg.toLowerCase().includes('draw') || msg.toLowerCase().includes('paint') || msg.toLowerCase().includes('sketch') || msg.toLowerCase().includes('portray') || msg.toLowerCase().includes('plot')) {
                        this.voiceRecognizer.draw(msg);
                    } else if (msg.toLowerCase().includes('transcript')) {
                        //this.voiceRecognizer.transcript(msg);
                    } else {
                        let lang: string = $('#select-languages option').filter(':selected').text();
                        this.voiceRecognizer.chat(msg, { "lang": lang });
                    }

                });

            }

        });

        $('#mute').on('click', (event) => {
            event.preventDefault();

            $(event.currentTarget).toggleClass('btn-warning btn-primary')
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

        $('#button-translate-message').on('click', (event) => {

            event.preventDefault();

            let msg = $('#textArea-chat-message').val().toString();
            let lang: string = $('#select-translation-language option').filter(':selected').text();

            if (msg.length > 0) {

                this.addToChatWindow(`Translate this into ${lang}: 
                                        <figure class="text-center mb-0">
                                            <blockquote class="blockquote">
                                                <p class="pb-3">
                                                    <i class="fas fa-quote-left fa-xs text-primary"></i>
                                                    <span class="lead font-italic" dir="auto">${msg}</span>
                                                    <i class="fas fa-quote-right fa-xs text-primary"></i>
                                                </p>
                                            </blockquote>
                                        </figure>`, this.userFirstName).then(() => {

                    let diagnostic = document.getElementsByClassName('list-unstyled custom-scrollbar').item(0) as HTMLElement;
                    let lastMsg = document.getElementsByClassName('direct-chat-msg');

                    diagnostic.scrollTo({ top: (lastMsg.item(lastMsg.length - 1) as HTMLElement).offsetTop, behavior: 'smooth' });

                    $('#textArea-chat-message').val('');

                    $('#ul-chat-attachments').html('');

                    this.voiceRecognizer.translate(msg, { "lang": lang });

                });
            }
        });
    }

    private addToChatWindow(textPage: string, userFirstName: string) {

        return new Promise((resolve, reject) => {
            try {
                let li: HTMLLIElement = document.createElement('li');
                li.classList.add(...['d-flex', 'justify-content-between', 'mb-2', 'direct-chat-msg']);
                li.innerHTML = `<img src="${this.profilePicture}" alt="avatar"
                                                     class="rounded-circle d-flex align-self-start me-3 shadow-1-strong" width="60">
                                                <div class="card w-100">
                                                    <div class="card-header d-flex justify-content-between">
                                                        <p class="fw-bold mb-0">${userFirstName}</p>
                                                        <p class="text-muted small mb-0"><i class="far fa-clock"></i> ${moment().format("D MMM h:mm a")}</p>
                                                    </div>
                                                    <div class="card-body">
                                                        <div class="mb-0" dir="auto">
                                                            ${textPage}
                                                        </div>
                                                    </div>
                                                </div>`;
                let msg_li = document.getElementsByClassName('list-unstyled custom-scrollbar').item(0).appendChild(li);
                return resolve(msg_li);
            } catch (e) {
                return reject();
            }
        });
    }

    private addToChatBox(textPage: string) {

        $('#textArea-chat-message').val(textPage).trigger('focus');
        //tinymce.activeEditor.setContent(`<p>${textPage}</p>`);
    }

    private addToAttachments(file: File) {

        return new Promise((resolve, reject) => {

            try {
                let filePath = URL.createObjectURL(file);
                let audio = new Audio(filePath);
                audio.preload = "metadata";

                audio.addEventListener('loadedmetadata', () => {
                    return resolve({ "filename": file.name, "audioduration": audio.duration, "audiosrc": audio.src });
                });

            } catch (e) {
                reject(e);
            }
        });
    }

    private transcript(file: File) {

        let formdata = new FormData();

        formdata.append("file", file);
        formdata.append("model", "whisper-1");

        document.getElementById('progress-pdf-parser').style.width = '0%';
        document.getElementById('progress-pdf-parser').setAttribute('aria-valuenow', '0');

        return $.ajax({
            type: 'POST',
            url: '/openai/transcript',
            processData: false,
            contentType: false,
            data: formdata,
            xhr: function () {
                var xhr = new window.XMLHttpRequest();
                //Upload progress
                xhr.upload.addEventListener("progress", function (evt) {
                    if (evt.lengthComputable) {
                        var percentComplete = evt.loaded / evt.total;

                        let percent_loaded = Math.ceil(percentComplete) * 100;
                        document.getElementById('progress-pdf-parser').style.width = `${percent_loaded}%`;
                        document.getElementById('progress-pdf-parser').setAttribute('aria-valuenow', percent_loaded.toFixed(2));

                        console.log(percentComplete);
                    }
                }, false);
                //Download progress
                xhr.addEventListener("progress", function (evt) {
                    if (evt.lengthComputable) {
                        var percentComplete = evt.loaded / evt.total;
                        //Do something with download progress
                        console.log(percentComplete);
                    }
                }, false);
                return xhr;
            }
        }).then((response, textStatus, xhr) => {
            if (xhr.status === 200) {

                let msg = response;
                return msg;

            }
        }).fail((XMLHttpRequest, textStatus, errorThrown) => {
            this.errMngr.logAjaxError(XMLHttpRequest, textStatus, errorThrown);
        });
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
                        return resolve(textPage);
                    });
                }, (reason) => {
                    // PDF loading error
                    return reject(reason);
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

                    document.getElementById('progress-pdf-parser').style.width = '0%';
                    document.getElementById('progress-pdf-parser').setAttribute('aria-valuenow', '0');

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
                    return resolve(finalString);
                });
            });
        });
    }

}

class VoiceRecognizer extends EventEmitter {

    private grammar: string;
    public diagnostic: HTMLElement;
    public recognition: SpeechRecognition;
    private speechRecognitionList: SpeechGrammarList;
    private voice: SpeechSynthesisVoice;
    private conversation: any[];
    private language: string = "en-GB";
    private errMngr: ErrorManager;
    private voices: SpeechSynthesisVoice[];

    constructor(private userFirstName: string, private profilePicture: string) {

        super();

        this.errMngr = new ErrorManager();

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
        this.voices = synth.getVoices();

        speechSynthesis.onvoiceschanged = () => {

            this.voices = speechSynthesis.getVoices();
            //console.log(...voices);
            let langs: string[] = Array.from(new Set(this.voices.map((voice) => { return voice.lang })));
            langs.sort();

            $('#select-translation-language').val('').multiselect({
                nonSelectedText: 'Please select language',
                disableIfEmpty: true,
                buttonClass: 'btn btn-primary',
                buttonWidth: '100%',
                maxHeight: 250,
                selectedClass: 'active multiselect-selected',
                includeSelectAllOption: false,
                buttonContainer: '<div class="multiselect-buttons btn-group d-flex w-100"></div>',
                templates: {
                    button: `<button type="button" class="multiselect dropdown-bordered dropdown-toggle dropdown-toggle-split" data-mdb-toggle="dropdown">
                                <span class="multiselect-selected-text"> </span>
                             </button>`,
                    ul: '<ul class="multiselect-container dropdown-menu custom-scrollbar w-100" ></ul>',
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
                    //Microsoft Libby Online (Natural) - English (United Kingdom)
                    //Microsoft Salma Online (Natural) - Arabic (Egypt)

                    this.voice = this.getVoice(this.voices, this.language);

                }
            });


            $('#select-languages').val('').multiselect({
                nonSelectedText: 'Please select language',
                disableIfEmpty: true,
                buttonClass: 'btn btn-success d-inline-block',
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

                    this.voice = this.getVoice(this.voices, this.language);
                }
            });

            let options: any[] = [];

            langs.forEach((lang, index) => {
                options.push({ label: lang, title: lang, value: index, selected: lang === this.language });
            });

            $('#select-translation-language').multiselect('dataprovider', options);
            $('#select-translation-language').multiselect('rebuild');

            $('#select-languages').multiselect('dataprovider', options);
            $('#select-languages').multiselect('rebuild');

            /*console.log*/(this.voices);

            if (!this.voice) {

                console.log($('#select-languages option:selected').text());

                this.voice = this.getVoice(this.voices, this.language);
            }
        };

        this.recognition.onresult = (event) => {

            let results = event.results;
            //const msg = results.item(results.length - 1)[0].transcript;

            for (const result of Array.from(event.results)) {
                // Print the transcription to the console
                const msg = result[0].transcript;

                this.diagnostic.innerHTML += `<li class="d-flex justify-content-between mb-2 direct-chat-msg" dir="auto">
                                                <img src="${profilePicture}" alt="avatar"
                                                     class="rounded-circle d-flex align-self-start me-3 shadow-1-strong" width="60">
                                                <div class="card w-100">
                                                    <div class="card-header d-flex justify-content-between">
                                                        <p class="fw-bold mb-0">${userFirstName}</p>
                                                        <p class="text-muted small mb-0"><i class="far fa-clock"></i> ${moment().format("D MMM h:mm a")}</p>
                                                    </div>
                                                    <div class="card-body">
                                                        <span lang="${this.language}" dir="auto">
                                                            ${msg}
                                                        </p>
                                                    </div>
                                                </div>
                                            </li>`;


                let lastMsg = document.getElementsByClassName('direct-chat-msg');

                this.diagnostic.scrollTo({ top: (lastMsg.item(lastMsg.length - 1) as HTMLElement).offsetTop, behavior: 'smooth' });

                if (msg.toLowerCase().includes('draw') || msg.toLowerCase().includes('paint') || msg.toLowerCase().includes('sketch') || msg.toLowerCase().includes('portray') || msg.toLowerCase().includes('plot')) {
                    this.draw(msg);
                } else {
                    let lang: string = $('#select-languages option').filter(':selected').text();
                    this.chat(`${msg}`, { "lang": lang });
                }
            }
        }

        this.conversation = [
            /*{ 'role': 'system', 'content': 'You are an assistant that can do translation. When doing translation, you ignore non-human languages like programming languages.' },*/
            { "role": "user", "content": `Good Morning, my name is ${userFirstName}.` },
            { "role": "assistant", "content": `Good morning ${userFirstName}, How may I assest you today?` },
            {"role": "user", "content": "What is your name?"},
            { "role": "assistant", "content": "My Name is Lucy." },
            {"role": "user", "content": "Hello Lucy."},
            { "role": "assistant", "content": `Hello ${userFirstName}.` }];
    }

    private getVoice(voices: SpeechSynthesisVoice[], languageCode: string) {

        let voice: SpeechSynthesisVoice;
        try {
            if (languageCode.startsWith('en')) {
                //Microsoft Libby Online (Natural) - English (United Kingdom)
                //Microsoft Salma Online (Natural) - Arabic (Egypt)
                voice = voices.filter((voice) => { return voice.lang.startsWith('en') && voice.name.includes('Libby'); })[0];
                console.log(voice.name);
            } else if (languageCode.startsWith('ar')) {
                voice = voices.filter((voice) => { return voice.lang.startsWith('ar') && voice.name.includes('Salma'); })[0];
                console.log(voice.name);
            } else if (languageCode.startsWith('es')) {
                voice = voices.filter((voice) => { return voice.lang.startsWith('es') && voice.name.includes('Elvira'); })[0];
                console.log(voice.name);
            } else if (languageCode.startsWith('hi')) {
                voice = voices.filter((voice) => { return voice.lang.startsWith('hi') && voice.name.includes('Swara'); })[0];
                console.log(voice.name);
            } else if (languageCode.startsWith('it')) {
                voice = voices.filter((voice) => { return voice.lang.startsWith('it') && voice.name.includes('Elsa'); })[0];
                console.log(voice.name);
            } else if (languageCode.startsWith('nl')) {
                voice = voices.filter((voice) => { return voice.lang.startsWith('nl') && voice.name.includes('Colette'); })[0];
                console.log(voice.name);
            } else if (languageCode.startsWith('ja')) {
                voice = voices.filter((voice) => { return voice.lang.startsWith('ja') && voice.name.includes('Nanami'); })[0];
                console.log(voice.name);
            } else {
                voice = voices.filter((voice) => { return voice.lang.includes(languageCode); })[0];
                console.log(voice.name);
            }
        } catch (e) {
            console.error(e);
            voice = voices.filter((voice) => { return voice.lang.includes(languageCode); })[0];
            console.log(voice.name);
        }
        return voice;
    }

    start() {
        this.recognition.start();
    }

    stop() {
        this.recognition.stop();
    }

    chat(prompt: string, options?: any) {

        if (prompt && prompt !== '') {
            this.conversation.push({ "role": "user", "content": prompt });
        }

        return $.ajax({
            type: 'POST',
            url: '/openai/chat',
            dataType:'json',
            data: {
                "model": "gpt-3.5-turbo",
                "messages": JSON.stringify(this.conversation)
            }
        }).then(async (response, textStatus, xhr) => {
            if (xhr.status === 200) {

                let msg = response;

                this.conversation.push({ "role": "assistant", "content": msg });

                let li = $(`<li class="d-flex justify-content-between mb-2 direct-chat-msg pull-right" dir="auto">
                                                <div class="card w-100">
                                                    <div class="card-header d-flex justify-content-between">
                                                        <p class="fw-bold mb-0">Lucy</p>
                                                        <a class="btn btn-sm btn-link ripple-surface btn-floating btn-read" data-mdb-toggle="collapse" href="#" role="button" aria-expanded="false" aria-controls="read" data-ripple-color="hsl(0, 0%, 67%)" style="">
                                                            <span class="material-icons md-18">record_voice_over</span>
                                                        </a>
                                                        <p class="text-muted small mb-0"><i class="far fa-clock"></i> ${moment().format("D MMM h:mm a")}</p>
                                                    </div>
                                                    <div class="card-body" style="font-family: 'Neo Sans Arabic', sans-serif;">
                                                        <div class="mb-0" dir="auto">
                                                             ${msg}
                                                        </div>
                                                    </div>
                                                </div>
                                                <img src="/img/Lucy.png" alt="avatar"
                                                     class="rounded-circle d-flex align-self-start ms-3 shadow-1-strong" width="60">
                                            </li>`);

                this.diagnostic.appendChild(li.get(0));

                let lastMsg = document.getElementsByClassName('direct-chat-msg');

                this.diagnostic.scrollTo({ top: (lastMsg.item(lastMsg.length - 1) as HTMLElement).offsetTop, behavior: 'smooth' });

                li.find('.btn-read').on('click', async (event) => {

                    event.preventDefault();

                    let current = event.currentTarget;

                    let container = $(current).closest('.card').find('.card-body div').last();
                    let message = container.text().trim();
                    this.voice = this.getVoice(this.voices, options.lang);
                    await this.speak({ "content": message, "language": options.lang, "container": container.get(0) as HTMLElement });
                });

                let container = li.find('.card').find('.card-body div').last();
                let message = container.text().trim();
                await this.speak({ "content": message, "language": options.lang, "container": container.get(0) as HTMLElement });
            }
        }).fail((XMLHttpRequest, textStatus, errorThrown) => {
            this.errMngr.logAjaxError(XMLHttpRequest, textStatus, errorThrown);
        });
    }

    draw(prompt: string, options?: any) {

        return $.ajax({
            type: 'POST',
            url: '/openai/draw',
            dataType: 'json',
            data: {"prompt": `${prompt}`,"n": "1","size": "1024x1024"}
        }).then((response, textStatus, xhr) => {
            if (xhr.status === 200) {

                this.diagnostic.innerHTML += `<li class="d-flex justify-content-between mb-2 direct-chat-msg pull-right">
                                                <div class="card w-100">
                                                    <div class="card-header d-flex justify-content-between">
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
                canvas.style.background = `url(${response})`;
                canvas.style.backgroundSize = 'contain';
                canvas.style.backgroundRepeat = 'no-repeat';
                canvas.style.backgroundPosition = 'center';

                let lastMsg = document.getElementsByClassName('direct-chat-msg');

                this.diagnostic.scrollTo({ top: (lastMsg.item(lastMsg.length - 1) as HTMLElement).offsetTop, behavior: 'smooth' });
            }
        }).fail((XMLHttpRequest, textStatus, errorThrown) => {
            this.errMngr.logAjaxError(XMLHttpRequest, textStatus, errorThrown);
        });
    }

    translate(prompt: string, options?: any) {

        if (prompt && prompt !== '') {
            this.conversation.push({
                "role": "user", "content": `Translate this into ${options.lang}: "${prompt}", and don't include the source text.'
                                            Return only the equivalent html code for the translation, separate each phrase in span tag with lang attribute and the direction attribute that match its recognized language based on context and narrative flow.
                                            Incluse the tags in div element with class named "translation-result" and add a class "text-end" to the div if the translation language is written from right to left.` });
        }

        return $.ajax({
            type: 'POST',
            url: '/openai/translate',
            dataType: 'json',
            data: {
                "model": "gpt-3.5-turbo",
                "messages": JSON.stringify(this.conversation)
            }
        }).then((response, textStatus, xhr) => {
            if (xhr.status === 200) {

                let msg = response;

                this.conversation.push({ "role": "assistant", "content": msg });

                let li = $(`<li class="d-flex justify-content-between mb-2 direct-chat-msg pull-right" dir="auto">
                                <div class="card w-100">
                                    <div class="card-header d-flex justify-content-between">
                                        <p class="fw-bold mb-0">Lucy</p>
                                        <a class="btn btn-sm btn-link ripple-surface btn-floating btn-read" data-mdb-toggle="collapse" href="#" role="button" aria-expanded="false" aria-controls="read" data-ripple-color="hsl(0, 0%, 67%)" style="">
                                            <span class="material-icons md-18">record_voice_over</span>
                                        </a>
                                        <p class="text-muted small mb-0"><i class="far fa-clock"></i> ${moment().format("D MMM h:mm a")}</p>
                                    </div>
                                    <div class="card-body" style="font-family: 'Neo Sans Arabic', sans-serif;">
                                        ${msg}
                                    </div>
                                </div>
                                <img src="/img/Lucy.png" alt="avatar"
                                        class="rounded-circle d-flex align-self-start ms-3 shadow-1-strong" width="60">
                            </li>`);

                this.diagnostic.appendChild(li.get(0))  ;

                let lastMsg = document.getElementsByClassName('direct-chat-msg');

                this.diagnostic.scrollTo({ top: (lastMsg.item(lastMsg.length - 1) as HTMLElement).offsetTop, behavior: 'smooth' });

                li.find('.btn-read').on('click', async(event) => {

                    event.preventDefault();

                    let current = event.currentTarget;

                    let container = $(current).closest('.card').find('.card-body .translation-result span'); // could be one or more span.

                    let message = container.text().trim();
                    this.voice = this.getVoice(this.voices, options.lang);
                    await this.speak({"content": message,  "language": options.lang, "container": container.get(0) as HTMLElement });
                });

                let translation = li.find('.card-body span');

                $.each(translation, async (index, span) => {
                    console.log(span.isConnected);
                    await this.speak({ "content": span.innerHTML.trim(), "language": span.lang, "container": span as HTMLElement });
                });
            }

        }).fail((XMLHttpRequest, textStatus, errorThrown) => {
            this.errMngr.logAjaxError(XMLHttpRequest, textStatus, errorThrown);
        });
    }

    speak(options?: any) {

        // https://jsfiddle.net/ourcodeworld/9k0z6m14/4/
        // https://codepen.io/tniezurawski/pen/wvzyVEE
        // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/String/match
        // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Array/splice
        // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/RegExp
        // https://jsfiddle.net/ourcodeworld/9k0z6m14/4/
        // https://linuxhint.com/highlight-text-using-javascript/#:~:text=For%20highlighting%20text%20in%20JavaScript%2C%20use%20the%20%E2%80%9Cmark%E2%80%9D%20element,script%3E%20tag%20or%20JavaScript%20file.

        return new Promise((resolve, reject) => {

            try {
                let utterance = new SpeechSynthesisUtterance(options.content);
                utterance.lang = options.language;
                utterance.voice = this.voice;
                utterance.rate = 1;
                utterance.pitch = 1;
                utterance.volume = 0.5;
                let wordIndex = 0;

                utterance.onend = (event) => {
                    try {

                        options.container.innerHTML = options.content;

                        if ($('#flexSwitchCheckChecked').is(':checked')) {
                            this.recognition.start();
                        } else {
                            this.recognition.stop();
                        }
                    } catch (e) {
                        console.log(e);
                    }

                    return resolve('complete');
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
                                        deviceId: { exact: audiooutput.deviceId },
                                        groupId: audiooutput.groupId
                                    }
                                };

                                navigator.mediaDevices.getUserMedia(constraints).then((stream: MediaStream) => {

                                    console.log(stream.getAudioTracks()[0].label);

                                    let equalizer = new Equalizer(this.profilePicture, stream);

                                    console.log('stream.active: ', stream.getAudioTracks().length);
                                }).catch(error => console.error(error));
                            }
                        });
                };

                utterance.onboundary = (event) => {

                    let word: string = this.getWordAt(options.content, event.charIndex);

                    try {
                        //options.container.innerText=msg;
                        let message = options.content.substring(0, event.charIndex) + "<span class='highlight'>" + word + "</span>" + options.content.substring(event.charIndex + word.length);
                        options.container.innerHTML = message;

                        let container = options.container.getElementsByClassName('highlight').item(0) as HTMLElement;
                        //container.style.background = "#FFF8D6";
                        container.style.color = '#616161';
                        container.style.fontWeight = '500';

                    } catch (e) {

                        return reject(e);
                    }

                    wordIndex++;
                }

                speechSynthesis.speak(utterance);

            } catch (e) {
                return reject(e);
            }
        });
    }

    getWordAt(str, pos) {
        // Perform type conversions.
        str = String(str);
        pos = Number(pos) >>> 0;

        // Search for the word's beginning and end.
        let left = str.slice(0, pos + 1).search(/\S+$/),
            right = str.slice(pos).search(/\s/);

        // The last word in the string is a special case.
        if (right < 0) {
            return str.slice(left);
        }
        // Return the word, using the located bounds to extract it from the string.
        return str.slice(left, right + pos);
    }
}

class Equalizer {

    private errMngr: ErrorManager;

    constructor(private profilePicture: string, stream?: MediaStream) {
        //$(document).on('click', 'img', () => {
        this.errMngr = new ErrorManager();

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
                        this.visualize(analyser);
                    })
                    .catch(function (err) {
                        console.log("The following gUM error occured: " + err);
                    });
            } else {
                console.log("getUserMedia not supported on your browser!");
            }
        } else {
            let source: MediaStreamAudioSourceNode;

            //console.log(stream.getAudioTracks().length + ', ' + JSON.stringify(stream.getAudioTracks()[0].getConstraints().deviceId) + ', ' + stream.getAudioTracks()[0].kind + stream.getAudioTracks()[0].label + ', ' + stream.id);
            source = audioCtx.createMediaStreamSource(stream.clone());
            source.connect(gainNode);
            gainNode.connect(analyser);

            this.visualize(analyser);
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

        let visualSetting = "sinewave";
        //console.log(visualSetting);

        if (visualSetting === "sinewave") {
            analyser.fftSize = 2048;
            const bufferLength = analyser.fftSize;
            //console.log(bufferLength);

            // We can use Float32Array instead of Uint8Array if we want higher precision
            // const dataArray = new Float32Array(bufferLength);
            const dataArray = new Uint8Array(bufferLength);

            const render = function () {

                // Set up canvas context for visualizer
                let canvas: HTMLCanvasElement = document.querySelector(".canvas-visualizer");
                let canvasCtx = canvas.getContext("2d");

                let intendedWidth = $("#visualizer-container").innerWidth().toString();
                canvas.setAttribute("width", intendedWidth);

                let WIDTH = canvas.width;
                let HEIGHT = canvas.height;
                canvasCtx.clearRect(0, 0, WIDTH, HEIGHT);

                let drawVisual = requestAnimationFrame(render);

                analyser.getByteTimeDomainData(dataArray);

                canvasCtx.fillStyle = "rgba(255, 255, 255, 0)";
                canvasCtx.fillRect(0, 0, WIDTH, HEIGHT);

                canvasCtx.lineWidth = 1;
                canvasCtx.strokeStyle = "rgba(220, 53, 69, 1)";

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
            //console.log(bufferLengthAlt);

            // See comment above for Float32Array()
            const dataArrayAlt = new Uint8Array(bufferLengthAlt);

            const drawAlt = function () {
                // Set up canvas context for visualizer
                let canvas: HTMLCanvasElement = document.querySelector(".canvas-visualizer");
                let canvasCtx = canvas.getContext("2d");

                let intendedWidth = document.getElementById("visualizer-container").clientWidth.toString();
                canvas.setAttribute("width", intendedWidth);
                let drawVisual;

                let WIDTH = canvas.width;
                let HEIGHT = canvas.height;
                canvasCtx.clearRect(0, 0, WIDTH, HEIGHT);
                drawVisual = requestAnimationFrame(drawAlt);

                analyser.getByteFrequencyData(dataArrayAlt);

                canvasCtx.fillStyle = "rgba(255, 255, 255, 1)";
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
            let canvas: HTMLCanvasElement = document.querySelector(".canvas-visualizer");
            let canvasCtx = canvas.getContext("2d");
            let WIDTH = canvas.width;
            let HEIGHT = canvas.height;

            canvasCtx.clearRect(0, 0, WIDTH, HEIGHT);
            canvasCtx.fillStyle = "red";
            canvasCtx.fillRect(0, 0, WIDTH, HEIGHT);
        }
    }

    visualizeD3(analyser: AnalyserNode) {

        //TODO: https://blog.scottlogic.com/2016/01/06/audio-api-with-d3.html
        // https://css-tricks.com/making-an-audio-waveform-visualizer-with-vanilla-javascript/
        // https://medium.com/swlh/visualizing-sound-with-d3-and-web-audio-api-435ffea88f30
        // https://github.com/willianjusten/awesome-audio-visualization

        let visualSetting = "sinewave";
        //console.log(visualSetting);

        switch (visualSetting) {
            case "sinewave": {
                analyser.fftSize = 2048;
                const bufferLength = analyser.fftSize;
                //console.log(bufferLength);

                // We can use Float32Array instead of Uint8Array if we want higher precision
                //const dataArray = new Float32Array(bufferLength);
                //const bufferLength = analyser.frequencyBinCount;
                const dataArray = new Uint8Array(bufferLength);

                const render = () => {

                    let drawVisual = requestAnimationFrame(render);

                    analyser.getByteFrequencyData(dataArray);


                };

                render();
            }

                break;

            case "rounded-sinewave": {
                analyser.fftSize = 2048;
                const bufferLength = analyser.fftSize;
                //console.log(bufferLength);

                // We can use Float32Array instead of Uint8Array if we want higher precision
                //const dataArray = new Float32Array(bufferLength);
                //const bufferLength = analyser.frequencyBinCount;
                const dataArray = new Uint8Array(bufferLength);

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
                    const radius = 85;

                    const length = 256//64;
                    const amplitude = 5;

                    const radialGenerator = d3.lineRadial()
                        .angle(d => d.angle)
                        .radius(d => d.radius)
                        .curve(d3.curveCardinalClosed)

                    const radialScale = d3.scaleLinear()
                        .domain([0, length])
                        .range([0, Math.PI * 2]);

                    const data = d3.range(length).map((d, i) => {
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

                    //https://stackoverflow.com/questions/20660085/how-to-stretch-an-image-in-a-svg-shape-to-fill-its-bounds
                    catpattern.append("image")
                        .attr("height", 70)
                        .attr("width", 70)
                        .attr("xlink:href", () => { return this.profilePicture; })
                        .attr("preserveAspectRatio", "xMidYMid slice");

                    vis.append("circle")
                        .attr("r", 35)
                        .attr("cy", 0)
                        .attr("cx", 0)
                        .attr('stroke', '#9575CD')
                        .attr('stroke-width', '3px')
                        .attr("fill", "url(#catpattern)");

                    //Mask approach
                    //https://codepen.io/tylersticka/pen/NWWqPmQ
                };

                render();
            }

                break;

            default:
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

                return resolve(navigator.mediaDevices.getUserMedia(constraints));
            }
        });
    }

    voiceChange(distortion, biquadFilter, audioCtx, echoDelay, gainNode, convolver) {
        distortion.oversample = "4x";
        biquadFilter.gain.setTargetAtTime(0, audioCtx.currentTime, 0);

        let voiceSetting = "off";
        //console.log(voiceSetting);

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

